using DeskBorder.Helpers;
using DeskBorder.Interop;
using DeskBorder.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.System;

namespace DeskBorder.Services;

public sealed partial class HotkeyService(ISettingsService settingsService, IFileLogService fileLogService) : IHotkeyService
{
    private const int ToggleDeskBorderEnabledHotkeyIdentifier = 1;
    private const int SwitchToPreviousDesktopHotkeyIdentifier = 2;
    private const int SwitchToNextDesktopHotkeyIdentifier = 3;
    private const int MoveFocusedWindowToPreviousDesktopHotkeyIdentifier = 4;
    private const int MoveFocusedWindowToNextDesktopHotkeyIdentifier = 5;
    private const int ToggleNavigatorHotkeyIdentifier = 6;
    private const uint RefreshRegisteredHotkeysMessage = Win32.WindowApplicationMessage + 1;
    private const uint ShutdownMessage = Win32.WindowApplicationMessage + 2;
    private const uint InvokeMouseHotkeyActionMessage = Win32.WindowApplicationMessage + 3;

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly ManualResetEventSlim _messageLoopReadySignal = new(false);
    private readonly Dictionary<HotkeyActionType, string?> _registrationFailureMessages = CreateEmptyRegistrationFailureMessages();
    private readonly List<RegisteredKeyboardHotkey> _registeredKeyboardHotkeys = [];
    private readonly List<RegisteredMouseHotkey> _registeredMouseHotkeys = [];
    private Thread? _messageLoopThread;
    private uint _messageLoopThreadIdentifier;
    private nint _mouseHookHandle;
    private Win32.LowLevelMouseHookProcedure? _mouseHookCallback;
    private bool _isDisposed;

    public event EventHandler<HotkeyInvokedEventArgs>? HotkeyInvoked;
    public event EventHandler? RegistrationStateChanged;

    public bool IsInitialized { get; private set; }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _fileLogService.WriteInformation(nameof(HotkeyService), "Disposing hotkey service.");
        _isDisposed = true;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;

        if (IsInitialized)
        {
            _ = TryPostControlMessage(ShutdownMessage);
            _messageLoopThread?.Join(TimeSpan.FromSeconds(2));
            IsInitialized = false;
        }

        _messageLoopReadySignal.Dispose();
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (IsInitialized)
            return;

        _fileLogService.WriteInformation(nameof(HotkeyService), "Initializing hotkey service.");
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;

        _messageLoopThread = new Thread(RunMessageLoopOnBackgroundThread)
        {
            IsBackground = true,
            Name = "DeskBorder Hotkey Message Loop"
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();

        if (!_messageLoopReadySignal.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The hotkey message loop did not start within the expected time.");

        IsInitialized = true;
        RefreshRegisteredHotkeys();
        _fileLogService.WriteInformation(nameof(HotkeyService), "Hotkey service initialized.");
    }

    public void RefreshRegisteredHotkeys()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!IsInitialized)
            throw new InvalidOperationException("The hotkey service has not been initialized.");

        _fileLogService.WriteInformation(nameof(HotkeyService), "Refreshing registered hotkeys.");
        if (TryPostControlMessage(RefreshRegisteredHotkeysMessage))
            return;

        var win32Exception = CreateWin32Exception("Failed to schedule the hotkey registration refresh.");
        _fileLogService.WriteError(nameof(HotkeyService), "Failed to schedule hotkey registration refresh.", win32Exception);
        throw win32Exception;
    }

    public string? GetRegistrationFailureMessage(HotkeyActionType hotkeyActionType) => _registrationFailureMessages.GetValueOrDefault(hotkeyActionType);

    private static void AddRegisteredHotkey(List<RegisteredKeyboardHotkey> registeredKeyboardHotkeys, List<RegisteredMouseHotkey> registeredMouseHotkeys, HotkeyActionType hotkeyActionType, int identifier, KeyboardShortcutSettings keyboardShortcutSettings)
    {
        if (!keyboardShortcutSettings.IsEnabled || !KeyboardShortcutHelper.IsKeyboardShortcutSpecified(keyboardShortcutSettings))
            return;

        if (keyboardShortcutSettings.TriggerType == KeyboardShortcutTriggerType.VirtualKey)
        {
            registeredKeyboardHotkeys.Add(new RegisteredKeyboardHotkey(
                identifier,
                hotkeyActionType,
                ConvertToNativeModifierMask(keyboardShortcutSettings.RequiredKeyboardModifierKeys),
                (uint)keyboardShortcutSettings.Key));
            return;
        }

        registeredMouseHotkeys.Add(new RegisteredMouseHotkey(
            hotkeyActionType,
            keyboardShortcutSettings.RequiredKeyboardModifierKeys,
            keyboardShortcutSettings.TriggerType));
    }

    private static HotkeyRegistrationPlan BuildRegisteredHotkeys(DeskBorderSettings settings)
    {
        var registeredKeyboardHotkeys = new List<RegisteredKeyboardHotkey>(6);
        var registeredMouseHotkeys = new List<RegisteredMouseHotkey>(6);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.ToggleDeskBorderEnabled,
            ToggleDeskBorderEnabledHotkeyIdentifier,
            settings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.SwitchToPreviousDesktop,
            SwitchToPreviousDesktopHotkeyIdentifier,
            settings.DesktopSwitchHotkeySettings.SwitchToPreviousDesktopHotkey);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.SwitchToNextDesktop,
            SwitchToNextDesktopHotkeyIdentifier,
            settings.DesktopSwitchHotkeySettings.SwitchToNextDesktopHotkey);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.MoveFocusedWindowToPreviousDesktop,
            MoveFocusedWindowToPreviousDesktopHotkeyIdentifier,
            settings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.MoveFocusedWindowToNextDesktop,
            MoveFocusedWindowToNextDesktopHotkeyIdentifier,
            settings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey);
        AddRegisteredHotkey(
            registeredKeyboardHotkeys,
            registeredMouseHotkeys,
            HotkeyActionType.ToggleNavigator,
            ToggleNavigatorHotkeyIdentifier,
            settings.NavigatorSettings.ToggleHotkey);
        ValidateRegisteredHotkeyCollisions(registeredKeyboardHotkeys, registeredMouseHotkeys);
        return new HotkeyRegistrationPlan(registeredKeyboardHotkeys, registeredMouseHotkeys);
    }

    private static uint ConvertToNativeModifierMask(KeyboardModifierKeys keyboardModifierKeys)
    {
        var nativeModifierMask = Win32.HotkeyModifierNoRepeat;
        if ((keyboardModifierKeys & KeyboardModifierKeys.Alternate) != 0)
            nativeModifierMask |= Win32.HotkeyModifierAlternate;

        if ((keyboardModifierKeys & KeyboardModifierKeys.Control) != 0)
            nativeModifierMask |= Win32.HotkeyModifierControl;

        if ((keyboardModifierKeys & KeyboardModifierKeys.Shift) != 0)
            nativeModifierMask |= Win32.HotkeyModifierShift;

        if ((keyboardModifierKeys & KeyboardModifierKeys.Windows) != 0)
            nativeModifierMask |= Win32.HotkeyModifierWindows;

        return nativeModifierMask;
    }

    private static Dictionary<HotkeyActionType, string?> CreateEmptyRegistrationFailureMessages() => new()
    {
        [HotkeyActionType.ToggleDeskBorderEnabled] = null,
        [HotkeyActionType.SwitchToPreviousDesktop] = null,
        [HotkeyActionType.SwitchToNextDesktop] = null,
        [HotkeyActionType.MoveFocusedWindowToPreviousDesktop] = null,
        [HotkeyActionType.MoveFocusedWindowToNextDesktop] = null,
        [HotkeyActionType.ToggleNavigator] = null
    };

    private static void ValidateRegisteredHotkeyCollisions(IReadOnlyList<RegisteredKeyboardHotkey> registeredKeyboardHotkeys, IReadOnlyList<RegisteredMouseHotkey> registeredMouseHotkeys)
    {
        var registeredKeyboardHotkeyKeys = new HashSet<(uint NativeModifierMask, uint NativeVirtualKey)>();
        foreach (var registeredKeyboardHotkey in registeredKeyboardHotkeys)
        {
            if (registeredKeyboardHotkeyKeys.Add((registeredKeyboardHotkey.NativeModifierMask, registeredKeyboardHotkey.NativeVirtualKey)))
                continue;

            throw new InvalidOperationException("Duplicate hotkey registrations are not supported.");
        }

        var registeredMouseHotkeyKeys = new HashSet<(KeyboardModifierKeys RequiredKeyboardModifierKeys, KeyboardShortcutTriggerType TriggerType)>();
        foreach (var registeredMouseHotkey in registeredMouseHotkeys)
        {
            if (registeredMouseHotkeyKeys.Add((registeredMouseHotkey.RequiredKeyboardModifierKeys, registeredMouseHotkey.TriggerType)))
                continue;

            throw new InvalidOperationException("Duplicate hotkey registrations are not supported.");
        }
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        var win32Exception = new Win32Exception(Marshal.GetLastWin32Error());
        return new Win32Exception(win32Exception.NativeErrorCode, $"{message} {win32Exception.Message}");
    }

    private void HandleHotkeyAction(HotkeyActionType hotkeyActionType)
    {
        _fileLogService.WriteInformation(nameof(HotkeyService), $"Hotkey invoked. Action={hotkeyActionType}.");
        HotkeyInvoked?.Invoke(this, new HotkeyInvokedEventArgs(hotkeyActionType));

        switch (hotkeyActionType)
        {
            case HotkeyActionType.ToggleDeskBorderEnabled:
                return;

            case HotkeyActionType.SwitchToPreviousDesktop:
                return;

            case HotkeyActionType.SwitchToNextDesktop:
                return;

            case HotkeyActionType.MoveFocusedWindowToPreviousDesktop:
                return;

            case HotkeyActionType.MoveFocusedWindowToNextDesktop:
                return;

            case HotkeyActionType.ToggleNavigator:
                return;

            default:
                throw new InvalidOperationException("The requested hotkey action is not supported.");
        }
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments)
    {
        if (_isDisposed || !IsInitialized)
            return;

        _fileLogService.WriteInformation(nameof(HotkeyService), "Scheduling hotkey refresh because settings changed.");
        _ = TryPostControlMessage(RefreshRegisteredHotkeysMessage);
    }

    private void InstallMouseHookCore(IReadOnlyList<RegisteredMouseHotkey> registeredMouseHotkeys)
    {
        _mouseHookCallback = OnMouseLowLevelHook;
        _mouseHookHandle = Win32.SetWindowsHookEx(Win32.LowLevelMouseHookId, _mouseHookCallback, 0, 0);
        if (_mouseHookHandle == 0)
            throw CreateWin32Exception("Failed to install the mouse hotkey hook.");

        _registeredMouseHotkeys.AddRange(registeredMouseHotkeys);
    }

    private nint OnMouseLowLevelHook(int code, nuint wParam, nint lParam)
    {
        if (code >= 0
            && TryGetKeyboardShortcutTriggerTypeFromMouseMessage(wParam, lParam, out var keyboardShortcutTriggerType)
            && TryGetRegisteredMouseHotkey(MouseHelper.GetModifierKeySnapshot().PressedKeyboardModifierKeys, keyboardShortcutTriggerType, out var registeredMouseHotkey))
        {
            if (!TryPostHotkeyActionMessage(registeredMouseHotkey.HotkeyActionType))
                _fileLogService.WriteWarning(nameof(HotkeyService), $"Failed to queue mouse hotkey action. Action={registeredMouseHotkey.HotkeyActionType}.");
        }

        return Win32.CallNextHookEx(_mouseHookHandle, code, wParam, lParam);
    }

    private void RegisterHotkeysCore(HotkeyRegistrationPlan hotkeyRegistrationPlan, Dictionary<HotkeyActionType, string?> registrationFailureMessages)
    {
        UnregisterMouseHookCore();
        UnregisterHotkeysCore();

        try
        {
            foreach (var registeredKeyboardHotkey in hotkeyRegistrationPlan.RegisteredKeyboardHotkeys)
            {
                if (!Win32.RegisterHotKey(0, registeredKeyboardHotkey.Identifier, registeredKeyboardHotkey.NativeModifierMask, registeredKeyboardHotkey.NativeVirtualKey))
                {
                    var registrationException = CreateWin32Exception($"Failed to register the {registeredKeyboardHotkey.HotkeyActionType} hotkey.");
                    registrationFailureMessages[registeredKeyboardHotkey.HotkeyActionType] = registrationException.Message;
                    throw registrationException;
                }

                _registeredKeyboardHotkeys.Add(registeredKeyboardHotkey);
            }

            if (hotkeyRegistrationPlan.RegisteredMouseHotkeys.Count > 0)
            {
                try { InstallMouseHookCore(hotkeyRegistrationPlan.RegisteredMouseHotkeys); }
                catch (Exception exception)
                {
                    foreach (var registeredMouseHotkey in hotkeyRegistrationPlan.RegisteredMouseHotkeys)
                        registrationFailureMessages[registeredMouseHotkey.HotkeyActionType] = exception.Message;

                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            _fileLogService.WriteError(nameof(HotkeyService), "Failed to register one or more hotkeys.", exception);
            UnregisterMouseHookCore();
            UnregisterHotkeysCore();
            throw;
        }
    }

    private void RunMessageLoopOnBackgroundThread()
    {
        _messageLoopThreadIdentifier = Win32.GetCurrentThreadId();
        _ = Win32.PeekMessage(out _, 0, 0, 0, Win32.PeekMessageNoRemove);
        _messageLoopReadySignal.Set();

        while (true)
        {
            var messageResult = Win32.GetMessage(out var nativeMessage, 0, 0, 0);
            if (messageResult == 0)
                return;

            if (messageResult < 0)
            {
                var win32Exception = CreateWin32Exception("The hotkey message loop failed to retrieve the next message.");
                _fileLogService.WriteError(nameof(HotkeyService), "The hotkey message loop failed to retrieve the next message.", win32Exception);
                throw win32Exception;
            }

            if (nativeMessage.Message == Win32.WindowHotkeyMessage)
            {
                if (TryGetRegisteredKeyboardHotkey((int)nativeMessage.WParam, out var registeredKeyboardHotkey))
                    HandleHotkeyAction(registeredKeyboardHotkey.HotkeyActionType);

                continue;
            }

            if (nativeMessage.Message == InvokeMouseHotkeyActionMessage)
            {
                var hotkeyActionTypeValue = (int)nativeMessage.WParam;
                if (Enum.IsDefined(typeof(HotkeyActionType), hotkeyActionTypeValue))
                    HandleHotkeyAction((HotkeyActionType)hotkeyActionTypeValue);

                continue;
            }

            if (nativeMessage.Message == RefreshRegisteredHotkeysMessage)
            {
                TryRefreshRegisteredHotkeysCore();
                continue;
            }

            if (nativeMessage.Message != ShutdownMessage)
                continue;

            UnregisterMouseHookCore();
            UnregisterHotkeysCore();
            Win32.PostQuitMessage(0);
        }
    }

    private static bool TryGetKeyboardShortcutTriggerTypeFromMouseMessage(nuint windowMessage, nint lParam, out KeyboardShortcutTriggerType keyboardShortcutTriggerType)
    {
        if (lParam == 0)
        {
            keyboardShortcutTriggerType = default;
            return false;
        }

        var nativeLowLevelMouseHookData = Marshal.PtrToStructure<Win32.NativeLowLevelMouseHookData>(lParam);
        if ((nativeLowLevelMouseHookData.Flags & Win32.LowLevelMouseHookInjectedFlag) != 0)
        {
            keyboardShortcutTriggerType = default;
            return false;
        }

        switch (windowMessage)
        {
            case Win32.LeftButtonDownWindowMessage:
                keyboardShortcutTriggerType = KeyboardShortcutTriggerType.MouseLeftButton;
                return true;

            case Win32.RightButtonDownWindowMessage:
                keyboardShortcutTriggerType = KeyboardShortcutTriggerType.MouseRightButton;
                return true;

            case Win32.MouseWheelWindowMessage:
                var mouseWheelDelta = unchecked((short)((nativeLowLevelMouseHookData.MouseData >> 16) & 0xFFFF));
                if (mouseWheelDelta == 0)
                    break;

                keyboardShortcutTriggerType = mouseWheelDelta > 0
                    ? KeyboardShortcutTriggerType.MouseWheelUp
                    : KeyboardShortcutTriggerType.MouseWheelDown;
                return true;
        }

        keyboardShortcutTriggerType = default;
        return false;
    }

    private bool TryGetRegisteredKeyboardHotkey(int identifier, out RegisteredKeyboardHotkey registeredKeyboardHotkey)
    {
        foreach (var currentRegisteredKeyboardHotkey in _registeredKeyboardHotkeys)
        {
            if (currentRegisteredKeyboardHotkey.Identifier != identifier)
                continue;

            registeredKeyboardHotkey = currentRegisteredKeyboardHotkey;
            return true;
        }

        registeredKeyboardHotkey = default;
        return false;
    }

    private bool TryGetRegisteredMouseHotkey(KeyboardModifierKeys pressedKeyboardModifierKeys, KeyboardShortcutTriggerType keyboardShortcutTriggerType, out RegisteredMouseHotkey registeredMouseHotkey)
    {
        foreach (var currentRegisteredMouseHotkey in _registeredMouseHotkeys)
        {
            if (currentRegisteredMouseHotkey.RequiredKeyboardModifierKeys != pressedKeyboardModifierKeys
                || currentRegisteredMouseHotkey.TriggerType != keyboardShortcutTriggerType)
            {
                continue;
            }

            registeredMouseHotkey = currentRegisteredMouseHotkey;
            return true;
        }

        registeredMouseHotkey = default;
        return false;
    }

    private bool TryPostControlMessage(uint message) => _messageLoopThreadIdentifier != 0 && Win32.PostThreadMessage(_messageLoopThreadIdentifier, message, 0, 0);

    private bool TryPostHotkeyActionMessage(HotkeyActionType hotkeyActionType) => _messageLoopThreadIdentifier != 0 && Win32.PostThreadMessage(_messageLoopThreadIdentifier, InvokeMouseHotkeyActionMessage, (nuint)hotkeyActionType, 0);

    private void TryRefreshRegisteredHotkeysCore()
    {
        var registrationFailureMessages = CreateEmptyRegistrationFailureMessages();
        try { RegisterHotkeysCore(BuildRegisteredHotkeys(_settingsService.Settings), registrationFailureMessages); }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(nameof(HotkeyService), "Hotkey refresh failed; all hotkeys were unregistered.", exception);
            UnregisterMouseHookCore();
            UnregisterHotkeysCore();
        }

        UpdateRegistrationFailureMessages(registrationFailureMessages);
    }

    private void UnregisterHotkeysCore()
    {
        foreach (var registeredKeyboardHotkey in _registeredKeyboardHotkeys)
            _ = Win32.UnregisterHotKey(0, registeredKeyboardHotkey.Identifier);

        _registeredKeyboardHotkeys.Clear();
    }

    private void UnregisterMouseHookCore()
    {
        if (_mouseHookHandle != 0)
            _ = Win32.UnhookWindowsHookEx(_mouseHookHandle);

        _mouseHookHandle = 0;
        _mouseHookCallback = null;
        _registeredMouseHotkeys.Clear();
    }

    private void UpdateRegistrationFailureMessages(IReadOnlyDictionary<HotkeyActionType, string?> registrationFailureMessages)
    {
        var hasChanged = false;
        foreach (var registrationFailureEntry in registrationFailureMessages)
        {
            if (string.Equals(_registrationFailureMessages[registrationFailureEntry.Key], registrationFailureEntry.Value, StringComparison.Ordinal))
                continue;

            _registrationFailureMessages[registrationFailureEntry.Key] = registrationFailureEntry.Value;
            hasChanged = true;
        }

        if (hasChanged)
        {
            foreach (var registrationFailureEntry in _registrationFailureMessages.Where(registrationFailureEntry => !string.IsNullOrWhiteSpace(registrationFailureEntry.Value)))
                _fileLogService.WriteWarning(nameof(HotkeyService), $"Hotkey registration warning for {registrationFailureEntry.Key}: {registrationFailureEntry.Value}");

            RegistrationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly record struct HotkeyRegistrationPlan(List<RegisteredKeyboardHotkey> RegisteredKeyboardHotkeys, List<RegisteredMouseHotkey> RegisteredMouseHotkeys);
    private readonly record struct RegisteredKeyboardHotkey(int Identifier, HotkeyActionType HotkeyActionType, uint NativeModifierMask, uint NativeVirtualKey);
    private readonly record struct RegisteredMouseHotkey(HotkeyActionType HotkeyActionType, KeyboardModifierKeys RequiredKeyboardModifierKeys, KeyboardShortcutTriggerType TriggerType);
}
