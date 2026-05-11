using DeskBorder.Helpers;
using DeskBorder.Interop;
using DeskBorder.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class KeyboardModifierAbsorptionService(IFileLogService fileLogService, ISettingsService settingsService) : IKeyboardModifierAbsorptionService
{
    private const int LeftShiftVirtualKey = 0xA0;
    private const int RightShiftVirtualKey = 0xA1;
    private const int LeftControlVirtualKey = 0xA2;
    private const int RightControlVirtualKey = 0xA3;
    private const int LeftAlternateVirtualKey = 0xA4;
    private const int RightAlternateVirtualKey = 0xA5;
    private const int LeftWindowsVirtualKey = 0x5B;
    private const int RightWindowsVirtualKey = 0x5C;
    private const uint ShutdownMessage = Win32.WindowApplicationMessage + 20;
    private static readonly TimeSpan s_messageLoopStartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_messageLoopShutdownTimeout = TimeSpan.FromSeconds(2);

    private sealed record PreemptivelyAbsorbedModifierKeyState(ushort ScanCode, bool IsConsumed);

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ManualResetEventSlim _messageLoopReadySignal = new(false);
    private readonly HashSet<uint> _heldNonModifierVirtualKeys = [];
    private readonly HashSet<int> _pendingAbsorbedModifierVirtualKeys = [];
    private readonly HashSet<int> _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys = [];
    private readonly Dictionary<int, PreemptivelyAbsorbedModifierKeyState> _preemptivelyAbsorbedModifierKeys = [];
    private readonly object _stateGate = new();
    private readonly ISettingsService _settingsService = settingsService;
    private Exception? _startupException;
    private Thread? _messageLoopThread;
    private uint _messageLoopThreadIdentifier;
    private nint _keyboardHookHandle;
    private Win32.LowLevelKeyboardHookProcedure? _keyboardHookCallback;
    private KeyboardModifierKeys _preemptiveAbsorptionKeyboardModifierKeys;
    private bool _isDisposed;

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        if (_isDisposed) return;

        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), "Disposing keyboard modifier absorption service.");
        _isDisposed = true;
        Stop();
        _messageLoopReadySignal.Dispose();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (IsRunning) return;

        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), "Starting keyboard modifier absorption service.");
        _startupException = null;
        _messageLoopReadySignal.Reset();
        RefreshPreemptiveAbsorptionState();
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        _messageLoopThread = new Thread(RunMessageLoopOnBackgroundThread)
        {
            IsBackground = true,
            Name = "DeskBorder Keyboard Modifier Absorption"
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();

        if (!_messageLoopReadySignal.Wait(s_messageLoopStartupTimeout))
        {
            _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
            Stop();
            throw new TimeoutException("The keyboard modifier absorption message loop did not start within the expected time.");
        }

        if (_startupException is not null)
        {
            _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
            _messageLoopThread.Join(s_messageLoopShutdownTimeout);
            _messageLoopThread = null;
            throw _startupException;
        }

        IsRunning = true;
        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), "Keyboard modifier absorption service started.");
    }

    public void Stop()
    {
        if (!IsRunning && _messageLoopThread is null) return;

        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), "Stopping keyboard modifier absorption service.");
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        if (_messageLoopThreadIdentifier != 0
            && !Win32.PostThreadMessage(_messageLoopThreadIdentifier, ShutdownMessage, 0, 0))
        {
            _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to schedule keyboard modifier absorption shutdown. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.");
        }

        if (_messageLoopThread is not null && !_messageLoopThread.Join(s_messageLoopShutdownTimeout))
            _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), "Timed out while stopping the keyboard modifier absorption hook.");

        _messageLoopThread = null;
        IsRunning = false;
        try { ReplayUnconsumedPreemptivelyAbsorbedModifierKeys(includeKeyUp: true); }
        catch (Exception exception) { _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), "Failed to replay unconsumed preemptively absorbed modifier keys while stopping the keyboard modifier absorption service.", exception); }

        ClearPendingAbsorptions();
        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), "Keyboard modifier absorption service stopped.");
    }

    public void AbsorbPressedKeyboardModifierKeys(KeyboardModifierKeys keyboardModifierKeys)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (keyboardModifierKeys == KeyboardModifierKeys.None) return;

        if (!IsRunning) Start();

        var preemptivelyConsumedModifierVirtualKeys = MarkPreemptivelyAbsorbedModifierKeysConsumed(keyboardModifierKeys);
        var preemptivelyConsumedModifierVirtualKeySet = preemptivelyConsumedModifierVirtualKeys.ToHashSet();
        var pressedModifierVirtualKeys = CreatePressedModifierVirtualKeys(keyboardModifierKeys)
            .Concat(preemptivelyConsumedModifierVirtualKeys)
            .Distinct()
            .ToArray();
        if (pressedModifierVirtualKeys.Length == 0) return;

        var immediatelyReleasedModifierVirtualKeys = pressedModifierVirtualKeys
            .Where(virtualKey => !preemptivelyConsumedModifierVirtualKeySet.Contains(virtualKey) && !IsWindowsVirtualKey(virtualKey))
            .ToArray();
        var pendingPhysicalModifierVirtualKeys = pressedModifierVirtualKeys
            .Where(virtualKey => !preemptivelyConsumedModifierVirtualKeySet.Contains(virtualKey))
            .ToArray();

        lock (_stateGate)
        {
            foreach (var pressedModifierVirtualKey in pendingPhysicalModifierVirtualKeys)
            {
                _pendingAbsorbedModifierVirtualKeys.Add(pressedModifierVirtualKey);
                if (IsWindowsVirtualKey(pressedModifierVirtualKey))
                    _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys.Add(pressedModifierVirtualKey);
            }
        }

        try
        {
            SendSyntheticModifierKeyUps(immediatelyReleasedModifierVirtualKeys);
        }
        catch
        {
            RemovePendingAbsorptions(pendingPhysicalModifierVirtualKeys);
            throw;
        }

        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Armed keyboard modifier absorption. ModifierKeys={keyboardModifierKeys}, PressedVirtualKeys={string.Join("|", pressedModifierVirtualKeys)}, PreemptivelyConsumedVirtualKeys={string.Join("|", preemptivelyConsumedModifierVirtualKeys)}.");
    }

    public bool AreRequiredModifierInputsPressed(ModifierGateSettings modifierGateSettings, ModifierKeySnapshot modifierKeySnapshot, MouseButtonSnapshot mouseButtonSnapshot) => AreRequiredKeyboardModifierKeysPressed(modifierGateSettings.RequiredKeyboardModifierKeys, modifierKeySnapshot.PressedKeyboardModifierKeys)
        && MouseHelper.AreRequiredMouseModifierButtonTriggersPressed(modifierGateSettings.RequiredMouseModifierButtonTriggers, mouseButtonSnapshot);

    public ModifierKeySnapshot GetModifierKeySnapshot()
    {
        var modifierKeySnapshot = CreateModifierKeySnapshot(GetPressedKeyboardModifierKeys());
        var preemptivelyAbsorbedKeyboardModifierKeys = GetPreemptivelyAbsorbedKeyboardModifierKeys();
        if (preemptivelyAbsorbedKeyboardModifierKeys == KeyboardModifierKeys.None) return modifierKeySnapshot;

        var pressedKeyboardModifierKeys = modifierKeySnapshot.PressedKeyboardModifierKeys | preemptivelyAbsorbedKeyboardModifierKeys;
        return CreateModifierKeySnapshot(pressedKeyboardModifierKeys);
    }

    public bool HasRequiredModifierInputs(ModifierGateSettings modifierGateSettings) => modifierGateSettings.RequiredKeyboardModifierKeys != KeyboardModifierKeys.None
        || modifierGateSettings.RequiredMouseModifierButtonTriggers.Length > 0;

    private static bool AreRequiredKeyboardModifierKeysPressed(KeyboardModifierKeys requiredKeyboardModifierKeys, KeyboardModifierKeys pressedKeyboardModifierKeys) => (pressedKeyboardModifierKeys & requiredKeyboardModifierKeys) == requiredKeyboardModifierKeys;

    private static ModifierKeySnapshot CreateModifierKeySnapshot(KeyboardModifierKeys pressedKeyboardModifierKeys) => new()
    {
        PressedKeyboardModifierKeys = pressedKeyboardModifierKeys,
        IsShiftPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Shift),
        IsControlPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Control),
        IsAlternatePressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Alternate),
        IsWindowsPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Windows)
    };

    private static Win32.NativeInput CreateKeyboardInput(ushort virtualKey, uint flags = 0, ushort scanCode = 0) => new()
    {
        Type = Win32.InputKeyboard,
        Data = new Win32.NativeInputUnion
        {
            KeyboardInput = new Win32.NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags
            }
        }
    };

    private static int[] CreatePressedModifierVirtualKeys(KeyboardModifierKeys keyboardModifierKeys)
    {
        var pressedModifierVirtualKeys = new List<int>();
        AddPressedModifierVirtualKey(pressedModifierVirtualKeys, keyboardModifierKeys, KeyboardModifierKeys.Shift, LeftShiftVirtualKey, RightShiftVirtualKey);
        AddPressedModifierVirtualKey(pressedModifierVirtualKeys, keyboardModifierKeys, KeyboardModifierKeys.Control, LeftControlVirtualKey, RightControlVirtualKey);
        AddPressedModifierVirtualKey(pressedModifierVirtualKeys, keyboardModifierKeys, KeyboardModifierKeys.Alternate, LeftAlternateVirtualKey, RightAlternateVirtualKey);
        AddPressedModifierVirtualKey(pressedModifierVirtualKeys, keyboardModifierKeys, KeyboardModifierKeys.Windows, LeftWindowsVirtualKey, RightWindowsVirtualKey);
        return [.. pressedModifierVirtualKeys];
    }

    private static void AddPressedModifierVirtualKey(List<int> pressedModifierVirtualKeys, KeyboardModifierKeys keyboardModifierKeys, KeyboardModifierKeys targetKeyboardModifierKey, int leftVirtualKey, int rightVirtualKey)
    {
        if (!keyboardModifierKeys.HasFlag(targetKeyboardModifierKey)) return;

        if (IsVirtualKeyPressed(leftVirtualKey)) pressedModifierVirtualKeys.Add(leftVirtualKey);

        if (IsVirtualKeyPressed(rightVirtualKey)) pressedModifierVirtualKeys.Add(rightVirtualKey);
    }

    private static bool IsModifierVirtualKey(uint virtualKey) => virtualKey == LeftShiftVirtualKey
        || virtualKey == RightShiftVirtualKey
        || virtualKey == LeftControlVirtualKey
        || virtualKey == RightControlVirtualKey
        || virtualKey == LeftAlternateVirtualKey
        || virtualKey == RightAlternateVirtualKey
        || virtualKey == LeftWindowsVirtualKey
        || virtualKey == RightWindowsVirtualKey;

    private static bool IsWindowsVirtualKey(int virtualKey) => virtualKey is LeftWindowsVirtualKey or RightWindowsVirtualKey;

    private static bool IsWindowsVirtualKey(uint virtualKey) => virtualKey == LeftWindowsVirtualKey || virtualKey == RightWindowsVirtualKey;

    private static uint GetKeyboardEventFlags(int virtualKey) => virtualKey switch
    {
        RightControlVirtualKey or RightAlternateVirtualKey or LeftWindowsVirtualKey or RightWindowsVirtualKey => Win32.KeyboardEventExtendedKeyFlag,
        _ => 0
    };

    private static bool IsVirtualKeyPressed(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & Win32.AsyncKeyDownMask) == Win32.AsyncKeyDownMask;

    private static KeyboardModifierKeys GetPressedKeyboardModifierKeys()
    {
        var pressedKeyboardModifierKeys = KeyboardModifierKeys.None;
        if (IsVirtualKeyPressed(LeftShiftVirtualKey) || IsVirtualKeyPressed(RightShiftVirtualKey)) pressedKeyboardModifierKeys |= KeyboardModifierKeys.Shift;

        if (IsVirtualKeyPressed(LeftControlVirtualKey) || IsVirtualKeyPressed(RightControlVirtualKey)) pressedKeyboardModifierKeys |= KeyboardModifierKeys.Control;

        if (IsVirtualKeyPressed(LeftAlternateVirtualKey) || IsVirtualKeyPressed(RightAlternateVirtualKey)) pressedKeyboardModifierKeys |= KeyboardModifierKeys.Alternate;

        if (IsVirtualKeyPressed(LeftWindowsVirtualKey) || IsVirtualKeyPressed(RightWindowsVirtualKey)) pressedKeyboardModifierKeys |= KeyboardModifierKeys.Windows;

        return pressedKeyboardModifierKeys;
    }

    private static bool IsInjectedKeyboardHookData(Win32.NativeLowLevelKeyboardHookData hookData)
        => (hookData.Flags & Win32.LowLevelKeyboardHookInjectedFlag) != 0
        || (hookData.Flags & Win32.LowLevelKeyboardHookLowerIntegrityInjectedFlag) != 0;

    private static bool IsKeyDownMessage(nuint message) => message == Win32.KeyDownWindowMessage || message == Win32.SystemKeyDownWindowMessage;

    private static bool IsKeyUpMessage(nuint message) => message == Win32.KeyUpWindowMessage || message == Win32.SystemKeyUpWindowMessage;

    private static string FormatLastWindowsErrorDetails(int lastWindowsErrorCode) => $"LastWindowsErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {new Win32Exception(lastWindowsErrorCode).Message})";

    private static KeyboardModifierKeys GetKeyboardModifierKey(int virtualKey) => virtualKey switch
    {
        LeftShiftVirtualKey or RightShiftVirtualKey => KeyboardModifierKeys.Shift,
        LeftControlVirtualKey or RightControlVirtualKey => KeyboardModifierKeys.Control,
        LeftAlternateVirtualKey or RightAlternateVirtualKey => KeyboardModifierKeys.Alternate,
        LeftWindowsVirtualKey or RightWindowsVirtualKey => KeyboardModifierKeys.Windows,
        _ => KeyboardModifierKeys.None
    };

    private static KeyboardModifierKeys GetKeyboardModifierKey(uint virtualKey) => GetKeyboardModifierKey((int)virtualKey);

    private static bool IsWindowsOnlyModifierGate(ModifierGateSettings modifierGateSettings) => modifierGateSettings.RequiredKeyboardModifierKeys == KeyboardModifierKeys.Windows
        && modifierGateSettings.RequiredMouseModifierButtonTriggers.Length == 0;

    private static bool ShouldPreemptivelyAbsorbWindowsKey(DeskBorderSettings settings) => settings.IsKeyboardModifierConsumptionAfterDesktopActionEnabled
        && (IsWindowsOnlyModifierGate(settings.SwitchDesktopModifierSettings)
            || (settings.IsDesktopCreationEnabled && IsWindowsOnlyModifierGate(settings.CreateDesktopModifierSettings)));

    private static KeyboardModifierKeys CreateActiveDesktopActionKeyboardModifierKeys(DeskBorderSettings settings)
    {
        var keyboardModifierKeys = settings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys;
        if (settings.IsDesktopCreationEnabled) keyboardModifierKeys |= settings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys;

        return keyboardModifierKeys;
    }

    private static KeyboardModifierKeys CreatePreemptiveAbsorptionKeyboardModifierKeys(DeskBorderSettings settings)
    {
        var preemptiveAbsorptionKeyboardModifierKeys = ShouldPreemptivelyAbsorbWindowsKey(settings)
            ? KeyboardModifierKeys.Windows
            : KeyboardModifierKeys.None;
        if (settings.IsKeyboardModifierConsumptionAfterDesktopActionEnabled && settings.IsNonWindowsKeyboardModifierPreemptiveAbsorptionEnabled) preemptiveAbsorptionKeyboardModifierKeys |= CreateActiveDesktopActionKeyboardModifierKeys(settings) & ~KeyboardModifierKeys.Windows;

        return preemptiveAbsorptionKeyboardModifierKeys;
    }

    private void ClearPendingAbsorptions()
    {
        lock (_stateGate)
        {
            _pendingAbsorbedModifierVirtualKeys.Clear();
            _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys.Clear();
            _preemptivelyAbsorbedModifierKeys.Clear();
            _heldNonModifierVirtualKeys.Clear();
        }
    }

    private void RemovePendingAbsorptions(IReadOnlyList<int> modifierVirtualKeys)
    {
        lock (_stateGate)
        {
            foreach (var modifierVirtualKey in modifierVirtualKeys)
            {
                _pendingAbsorbedModifierVirtualKeys.Remove(modifierVirtualKey);
                _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys.Remove(modifierVirtualKey);
            }
        }
    }

    private bool TryConsumePendingPhysicalModifierKeyUp(uint virtualKey, out bool shouldSendSyntheticKeyUp)
    {
        lock (_stateGate)
        {
            shouldSendSyntheticKeyUp = _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys.Remove((int)virtualKey);
            return _pendingAbsorbedModifierVirtualKeys.Remove((int)virtualKey);
        }
    }

    private bool TryBeginPreemptiveModifierKeyAbsorption(Win32.NativeLowLevelKeyboardHookData hookData)
    {
        var keyboardModifierKey = GetKeyboardModifierKey(hookData.VirtualKey);
        if (keyboardModifierKey == KeyboardModifierKeys.None || !_preemptiveAbsorptionKeyboardModifierKeys.HasFlag(keyboardModifierKey)) return false;
        if (HasHeldNonModifierKeys()) return false;
        if (keyboardModifierKey == KeyboardModifierKeys.Windows && HasActiveNonWindowsModifierKey()) return false;

        var hasNewAbsorptionStarted = false;
        lock (_stateGate)
        {
            var virtualKey = (int)hookData.VirtualKey;
            if (!_preemptivelyAbsorbedModifierKeys.ContainsKey(virtualKey))
            {
                _preemptivelyAbsorbedModifierKeys[virtualKey] = new((ushort)hookData.ScanCode, false);
                hasNewAbsorptionStarted = true;
            }
        }

        if (hasNewAbsorptionStarted) _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Preemptively absorbed physical modifier key-down. ModifierKey={keyboardModifierKey}, VirtualKey={hookData.VirtualKey}.");

        return true;
    }

    private bool HasHeldNonModifierKeys()
    {
        lock (_stateGate)
        {
            foreach (var heldNonModifierVirtualKey in _heldNonModifierVirtualKeys.ToArray())
            {
                if (!IsVirtualKeyPressed((int)heldNonModifierVirtualKey)) _heldNonModifierVirtualKeys.Remove(heldNonModifierVirtualKey);
            }

            return _heldNonModifierVirtualKeys.Count > 0;
        }
    }

    private bool HasActiveNonWindowsModifierKey()
    {
        if ((GetPreemptivelyAbsorbedKeyboardModifierKeys() & (KeyboardModifierKeys.Shift | KeyboardModifierKeys.Control | KeyboardModifierKeys.Alternate)) != KeyboardModifierKeys.None) return true;

        return IsVirtualKeyPressed(LeftShiftVirtualKey)
            || IsVirtualKeyPressed(RightShiftVirtualKey)
            || IsVirtualKeyPressed(LeftControlVirtualKey)
            || IsVirtualKeyPressed(RightControlVirtualKey)
            || IsVirtualKeyPressed(LeftAlternateVirtualKey)
            || IsVirtualKeyPressed(RightAlternateVirtualKey);
    }

    private KeyboardModifierKeys GetPreemptivelyAbsorbedKeyboardModifierKeys()
    {
        var keyboardModifierKeys = KeyboardModifierKeys.None;
        lock (_stateGate)
        {
            foreach (var virtualKey in _preemptivelyAbsorbedModifierKeys.Keys) keyboardModifierKeys |= GetKeyboardModifierKey(virtualKey);
        }

        return keyboardModifierKeys;
    }

    private int[] MarkPreemptivelyAbsorbedModifierKeysConsumed(KeyboardModifierKeys keyboardModifierKeys)
    {
        var consumedModifierVirtualKeys = new List<int>();
        lock (_stateGate)
        {
            foreach (var preemptivelyAbsorbedModifierKey in _preemptivelyAbsorbedModifierKeys.ToArray())
            {
                var preemptivelyAbsorbedKeyboardModifierKey = GetKeyboardModifierKey(preemptivelyAbsorbedModifierKey.Key);
                if (preemptivelyAbsorbedKeyboardModifierKey == KeyboardModifierKeys.None || !keyboardModifierKeys.HasFlag(preemptivelyAbsorbedKeyboardModifierKey)) continue;

                _preemptivelyAbsorbedModifierKeys[preemptivelyAbsorbedModifierKey.Key] = preemptivelyAbsorbedModifierKey.Value with { IsConsumed = true };
                consumedModifierVirtualKeys.Add(preemptivelyAbsorbedModifierKey.Key);
            }
        }

        return [.. consumedModifierVirtualKeys];
    }

    private bool TryConsumePreemptivelyAbsorbedModifierKeyUp(uint virtualKey, out ushort scanCode, out bool isConsumed)
    {
        lock (_stateGate)
        {
            scanCode = 0;
            isConsumed = false;
            if (!_preemptivelyAbsorbedModifierKeys.TryGetValue((int)virtualKey, out var modifierKeyState)) return false;

            scanCode = modifierKeyState.ScanCode;
            isConsumed = modifierKeyState.IsConsumed;
            _preemptivelyAbsorbedModifierKeys.Remove((int)virtualKey);
            return true;
        }
    }

    private bool TryRemovePendingModifierAbsorption(uint virtualKey)
    {
        lock (_stateGate)
        {
            _pendingSyntheticKeyUpAfterPhysicalKeyUpVirtualKeys.Remove((int)virtualKey);
            return _pendingAbsorbedModifierVirtualKeys.Remove((int)virtualKey);
        }
    }

    private bool ReplayUnconsumedPreemptivelyAbsorbedModifierKeys(bool includeKeyUp)
    {
        List<(int VirtualKey, ushort ScanCode)> replayedModifierVirtualKeys = [];
        lock (_stateGate)
        {
            foreach (var preemptivelyAbsorbedModifierKey in _preemptivelyAbsorbedModifierKeys.ToArray())
            {
                if (preemptivelyAbsorbedModifierKey.Value.IsConsumed) continue;

                replayedModifierVirtualKeys.Add((preemptivelyAbsorbedModifierKey.Key, preemptivelyAbsorbedModifierKey.Value.ScanCode));
                _preemptivelyAbsorbedModifierKeys.Remove(preemptivelyAbsorbedModifierKey.Key);
            }
        }

        if (replayedModifierVirtualKeys.Count == 0) return false;

        SendSyntheticModifierKeyReplay(replayedModifierVirtualKeys, includeKeyUp);
        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Replayed unconsumed preemptively absorbed modifier keys. VirtualKeys={string.Join("|", replayedModifierVirtualKeys.Select(modifierVirtualKey => modifierVirtualKey.VirtualKey))}, IncludeKeyUp={includeKeyUp}.");
        return true;
    }

    private void SendSyntheticModifierKeyReplay(IReadOnlyList<(int VirtualKey, ushort ScanCode)> modifierVirtualKeys, bool includeKeyUp)
    {
        if (modifierVirtualKeys.Count == 0) return;

        var keyboardInputs = new List<Win32.NativeInput>(includeKeyUp ? modifierVirtualKeys.Count * 2 : modifierVirtualKeys.Count);
        foreach (var modifierVirtualKey in modifierVirtualKeys) keyboardInputs.Add(CreateKeyboardInput((ushort)modifierVirtualKey.VirtualKey, GetKeyboardEventFlags(modifierVirtualKey.VirtualKey), modifierVirtualKey.ScanCode));

        if (includeKeyUp)
        {
            foreach (var modifierVirtualKey in modifierVirtualKeys.Reverse()) keyboardInputs.Add(CreateKeyboardInput((ushort)modifierVirtualKey.VirtualKey, Win32.KeyboardEventKeyUpFlag | GetKeyboardEventFlags(modifierVirtualKey.VirtualKey), modifierVirtualKey.ScanCode));
        }

        SendSyntheticKeyboardInputs(keyboardInputs, [.. modifierVirtualKeys.Select(modifierVirtualKey => modifierVirtualKey.VirtualKey)]);
    }

    private void SendSyntheticModifierKeyUps(IReadOnlyList<int> modifierVirtualKeys)
    {
        if (modifierVirtualKeys.Count == 0) return;

        var keyboardInputs = modifierVirtualKeys
            .Select(virtualKey => CreateKeyboardInput((ushort)virtualKey, Win32.KeyboardEventKeyUpFlag | GetKeyboardEventFlags(virtualKey)))
            .ToArray();
        SendSyntheticKeyboardInputs(keyboardInputs, modifierVirtualKeys);
    }

    private void SendSyntheticKeyboardInputs(IReadOnlyList<Win32.NativeInput> keyboardInputs, IReadOnlyList<int> modifierVirtualKeys)
    {
        if (keyboardInputs.Count == 0) return;

        var nativeInputSize = Marshal.SizeOf<Win32.NativeInput>();
        var sentInputCount = Win32.SendInput((uint)keyboardInputs.Count, [.. keyboardInputs], nativeInputSize);
        if (sentInputCount != keyboardInputs.Count) throw CreateKeyboardModifierAbsorptionException(modifierVirtualKeys, keyboardInputs, sentInputCount, nativeInputSize);
    }

    private InvalidOperationException CreateKeyboardModifierAbsorptionException(IReadOnlyList<int> modifierVirtualKeys, IReadOnlyList<Win32.NativeInput> keyboardInputs, uint sentInputCount, int nativeInputSize)
    {
        var lastWindowsErrorCode = Marshal.GetLastWin32Error();
        var lastWindowsErrorMessage = new Win32Exception(lastWindowsErrorCode).Message;
        var keyboardInputSummaries = string.Join(", ", keyboardInputs.Select(CreateKeyboardInputSummary));
        return new($"Unable to absorb the pressed keyboard modifier input. ModifierVirtualKeys={string.Join("|", modifierVirtualKeys)}, RequestedInputCount={keyboardInputs.Count}, SentInputCount={sentInputCount}, NativeInputSize={nativeInputSize}, LastWindowsErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {lastWindowsErrorMessage}), KeyboardInputs=[{keyboardInputSummaries}].");
    }

    private static string CreateKeyboardInputSummary(Win32.NativeInput keyboardInput)
    {
        var keyboardData = keyboardInput.Data.KeyboardInput;
        return $"Type={keyboardInput.Type}, VirtualKey={keyboardData.VirtualKey}, ScanCode={keyboardData.ScanCode}, Flags=0x{keyboardData.Flags:X8}, Time={keyboardData.Time}, ExtraInfo={keyboardData.ExtraInfo}";
    }

    private void TrackHeldNonModifierKey(Win32.NativeLowLevelKeyboardHookData hookData, nuint message)
    {
        if (IsModifierVirtualKey(hookData.VirtualKey)) return;

        lock (_stateGate)
        {
            if (IsKeyDownMessage(message)) _heldNonModifierVirtualKeys.Add(hookData.VirtualKey);
            else if (IsKeyUpMessage(message)) _heldNonModifierVirtualKeys.Remove(hookData.VirtualKey);
        }
    }

    private nint OnKeyboardLowLevelHook(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && (IsKeyDownMessage(wParam) || IsKeyUpMessage(wParam)))
        {
            var hookData = Marshal.PtrToStructure<Win32.NativeLowLevelKeyboardHookData>(lParam);
            if (!IsInjectedKeyboardHookData(hookData))
            {
                TrackHeldNonModifierKey(hookData, wParam);

                if (IsKeyDownMessage(wParam))
                {
                    if (IsModifierVirtualKey(hookData.VirtualKey) && TryBeginPreemptiveModifierKeyAbsorption(hookData)) return 1;

                    try { ReplayUnconsumedPreemptivelyAbsorbedModifierKeys(includeKeyUp: false); }
                    catch (Exception exception) { _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to replay preemptively absorbed modifier keys before forwarding a physical key-down. VirtualKey={hookData.VirtualKey}.", exception); }

                    if (IsModifierVirtualKey(hookData.VirtualKey) && TryRemovePendingModifierAbsorption(hookData.VirtualKey))
                        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Cleared stale keyboard modifier absorption on a new physical key-down. VirtualKey={hookData.VirtualKey}.");

                    return Win32.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
                }

                if (!IsModifierVirtualKey(hookData.VirtualKey)) return Win32.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);

                if (TryConsumePreemptivelyAbsorbedModifierKeyUp(hookData.VirtualKey, out var scanCode, out var isConsumed))
                {
                    if (!isConsumed)
                    {
                        try { SendSyntheticModifierKeyReplay([((int)hookData.VirtualKey, scanCode)], includeKeyUp: true); }
                        catch (Exception exception) { _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to replay an unconsumed preemptively absorbed modifier key on physical key-up. VirtualKey={hookData.VirtualKey}.", exception); }
                    }
                    else
                    {
                        try { SendSyntheticModifierKeyUps([(int)hookData.VirtualKey]); }
                        catch (Exception exception) { _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to send synthetic modifier key-up after consuming a preemptively absorbed modifier key. VirtualKey={hookData.VirtualKey}.", exception); }
                    }

                    _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Absorbed physical modifier key-up after preemptive absorption. VirtualKey={hookData.VirtualKey}, WasConsumed={isConsumed}.");
                    return 1;
                }

                if (TryConsumePendingPhysicalModifierKeyUp(hookData.VirtualKey, out var shouldSendSyntheticKeyUp))
                {
                    if (shouldSendSyntheticKeyUp)
                    {
                        try { SendSyntheticModifierKeyUps([(int)hookData.VirtualKey]); }
                        catch (Exception exception) { _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to send synthetic modifier key-up after absorbing physical key-up. VirtualKey={hookData.VirtualKey}.", exception); }
                    }

                    _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Absorbed physical keyboard modifier key-up. VirtualKey={hookData.VirtualKey}, SyntheticKeyUpSent={shouldSendSyntheticKeyUp}.");
                    return 1;
                }
            }
        }

        return Win32.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
    }

    private void RefreshPreemptiveAbsorptionState()
    {
        _preemptiveAbsorptionKeyboardModifierKeys = CreatePreemptiveAbsorptionKeyboardModifierKeys(_settingsService.Settings);
        _fileLogService.WriteInformation(nameof(KeyboardModifierAbsorptionService), $"Refreshed keyboard modifier preemptive absorption state. ModifierKeys={_preemptiveAbsorptionKeyboardModifierKeys}.");
    }

    private void OnSettingsServiceSettingsChanged(object? _, EventArgs __) => RefreshPreemptiveAbsorptionState();

    private void RunMessageLoopOnBackgroundThread()
    {
        try
        {
            _messageLoopThreadIdentifier = Win32.GetCurrentThreadId();
            _ = Win32.PeekMessage(out _, 0, 0, 0, Win32.PeekMessageNoRemove);
            _keyboardHookCallback = OnKeyboardLowLevelHook;
            _keyboardHookHandle = Win32.SetWindowsHookEx(Win32.LowLevelKeyboardHookId, _keyboardHookCallback, 0, 0);
            if (_keyboardHookHandle == 0)
            {
                _startupException = new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the keyboard modifier absorption hook.");
                _messageLoopReadySignal.Set();
                return;
            }

            _messageLoopReadySignal.Set();
            RunMessageLoopCore();
        }
        catch (Exception exception)
        {
            if (!_messageLoopReadySignal.IsSet)
                _startupException = exception;

            _fileLogService.WriteError(nameof(KeyboardModifierAbsorptionService), "The keyboard modifier absorption hook failed unexpectedly.", exception);
            _messageLoopReadySignal.Set();
        }
        finally
        {
            UninstallKeyboardHook();
            ClearPendingAbsorptions();
            _keyboardHookCallback = null;
            _messageLoopThreadIdentifier = 0;
        }
    }

    private void RunMessageLoopCore()
    {
        while (true)
        {
            var messageResult = Win32.GetMessage(out var nativeMessage, 0, 0, 0);
            if (messageResult == 0) return;

            if (messageResult < 0)
            {
                var win32Exception = new Win32Exception(Marshal.GetLastWin32Error(), "The keyboard modifier absorption message loop failed to retrieve the next message.");
                _fileLogService.WriteError(nameof(KeyboardModifierAbsorptionService), "The keyboard modifier absorption message loop failed to retrieve the next message.", win32Exception);
                return;
            }

            if (nativeMessage.Message != ShutdownMessage) continue;

            Win32.PostQuitMessage(0);
            return;
        }
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHookHandle == 0) return;

        var keyboardHookHandle = _keyboardHookHandle;
        _keyboardHookHandle = 0;
        if (!Win32.UnhookWindowsHookEx(keyboardHookHandle))
            _fileLogService.WriteWarning(nameof(KeyboardModifierAbsorptionService), $"Failed to uninstall keyboard modifier absorption hook. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.");
    }
}
