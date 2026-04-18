using DeskBorder.Interop;
using DeskBorder.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.System;

namespace DeskBorder.Services;

public sealed partial class HotkeyService(ISettingsService settingsService) : IHotkeyService
{
    private const int ToggleDeskBorderEnabledHotkeyIdentifier = 1;
    private const int MoveFocusedWindowToPreviousDesktopHotkeyIdentifier = 2;
    private const int MoveFocusedWindowToNextDesktopHotkeyIdentifier = 3;
    private const int ToggleNavigatorHotkeyIdentifier = 4;
    private const uint RefreshRegisteredHotkeysMessage = Win32.WindowApplicationMessage + 1;
    private const uint ShutdownMessage = Win32.WindowApplicationMessage + 2;

    private readonly ISettingsService _settingsService = settingsService;
    private readonly ManualResetEventSlim _messageLoopReadySignal = new(false);
    private readonly Dictionary<HotkeyActionType, string?> _registrationFailureMessages = CreateEmptyRegistrationFailureMessages();
    private readonly List<RegisteredHotkey> _registeredHotkeys = [];
    private Thread? _messageLoopThread;
    private uint _messageLoopThreadIdentifier;
    private bool _isDisposed;

    public event EventHandler<HotkeyInvokedEventArgs>? HotkeyInvoked;
    public event EventHandler? RegistrationStateChanged;

    public bool IsInitialized { get; private set; }

    public void Dispose()
    {
        if (_isDisposed)
            return;

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
    }

    public void RefreshRegisteredHotkeys()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!IsInitialized)
            throw new InvalidOperationException("The hotkey service has not been initialized.");

        if (TryPostControlMessage(RefreshRegisteredHotkeysMessage))
            return;

        throw CreateWin32Exception("Failed to schedule the hotkey registration refresh.");
    }

    public string? GetRegistrationFailureMessage(HotkeyActionType hotkeyActionType) => _registrationFailureMessages.GetValueOrDefault(hotkeyActionType);

    private static void AddRegisteredHotkey(List<RegisteredHotkey> registeredHotkeys, HotkeyActionType hotkeyActionType, int identifier, KeyboardShortcutSettings keyboardShortcutSettings)
    {
        if (!keyboardShortcutSettings.IsEnabled || keyboardShortcutSettings.Key == VirtualKey.None)
            return;

        registeredHotkeys.Add(new RegisteredHotkey(
            identifier,
            hotkeyActionType,
            ConvertToNativeModifierMask(keyboardShortcutSettings.RequiredKeyboardModifierKeys),
            (uint)keyboardShortcutSettings.Key));
    }

    private static List<RegisteredHotkey> BuildRegisteredHotkeys(DeskBorderSettings settings)
    {
        var registeredHotkeys = new List<RegisteredHotkey>(4);
        AddRegisteredHotkey(
            registeredHotkeys,
            HotkeyActionType.ToggleDeskBorderEnabled,
            ToggleDeskBorderEnabledHotkeyIdentifier,
            settings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey);
        AddRegisteredHotkey(
            registeredHotkeys,
            HotkeyActionType.MoveFocusedWindowToPreviousDesktop,
            MoveFocusedWindowToPreviousDesktopHotkeyIdentifier,
            settings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey);
        AddRegisteredHotkey(
            registeredHotkeys,
            HotkeyActionType.MoveFocusedWindowToNextDesktop,
            MoveFocusedWindowToNextDesktopHotkeyIdentifier,
            settings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey);
        AddRegisteredHotkey(
            registeredHotkeys,
            HotkeyActionType.ToggleNavigator,
            ToggleNavigatorHotkeyIdentifier,
            settings.NavigatorSettings.ToggleHotkey);
        ValidateRegisteredHotkeyCollisions(registeredHotkeys);
        return registeredHotkeys;
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
        [HotkeyActionType.MoveFocusedWindowToPreviousDesktop] = null,
        [HotkeyActionType.MoveFocusedWindowToNextDesktop] = null,
        [HotkeyActionType.ToggleNavigator] = null
    };

    private static void ValidateRegisteredHotkeyCollisions(IReadOnlyList<RegisteredHotkey> registeredHotkeys)
    {
        var registeredHotkeyKeys = new HashSet<(uint NativeModifierMask, uint NativeVirtualKey)>();
        foreach (var registeredHotkey in registeredHotkeys)
        {
            if (registeredHotkeyKeys.Add((registeredHotkey.NativeModifierMask, registeredHotkey.NativeVirtualKey)))
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
        HotkeyInvoked?.Invoke(this, new HotkeyInvokedEventArgs(hotkeyActionType));

        switch (hotkeyActionType)
        {
            case HotkeyActionType.ToggleDeskBorderEnabled:
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

        _ = TryPostControlMessage(RefreshRegisteredHotkeysMessage);
    }

    private void RegisterHotkeysCore(IReadOnlyList<RegisteredHotkey> registeredHotkeys, Dictionary<HotkeyActionType, string?> registrationFailureMessages)
    {
        UnregisterHotkeysCore();

        try
        {
            foreach (var registeredHotkey in registeredHotkeys)
            {
                if (!Win32.RegisterHotKey(0, registeredHotkey.Identifier, registeredHotkey.NativeModifierMask, registeredHotkey.NativeVirtualKey))
                {
                    var registrationException = CreateWin32Exception($"Failed to register the {registeredHotkey.HotkeyActionType} hotkey.");
                    registrationFailureMessages[registeredHotkey.HotkeyActionType] = registrationException.Message;
                    throw registrationException;
                }

                _registeredHotkeys.Add(registeredHotkey);
            }
        }
        catch
        {
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
                throw CreateWin32Exception("The hotkey message loop failed to retrieve the next message.");

            if (nativeMessage.Message == Win32.WindowHotkeyMessage)
            {
                if (TryGetRegisteredHotkey((int)nativeMessage.WParam, out var registeredHotkey))
                    HandleHotkeyAction(registeredHotkey.HotkeyActionType);

                continue;
            }

            if (nativeMessage.Message == RefreshRegisteredHotkeysMessage)
            {
                TryRefreshRegisteredHotkeysCore();
                continue;
            }

            if (nativeMessage.Message != ShutdownMessage)
                continue;

            UnregisterHotkeysCore();
            Win32.PostQuitMessage(0);
        }
    }

    private bool TryGetRegisteredHotkey(int identifier, out RegisteredHotkey registeredHotkey)
    {
        foreach (var currentRegisteredHotkey in _registeredHotkeys)
        {
            if (currentRegisteredHotkey.Identifier != identifier)
                continue;

            registeredHotkey = currentRegisteredHotkey;
            return true;
        }

        registeredHotkey = default;
        return false;
    }

    private bool TryPostControlMessage(uint message) => _messageLoopThreadIdentifier != 0 && Win32.PostThreadMessage(_messageLoopThreadIdentifier, message, 0, 0);

    private void TryRefreshRegisteredHotkeysCore()
    {
        var registrationFailureMessages = CreateEmptyRegistrationFailureMessages();
        try { RegisterHotkeysCore(BuildRegisteredHotkeys(_settingsService.Settings), registrationFailureMessages); }
        catch { UnregisterHotkeysCore(); }

        UpdateRegistrationFailureMessages(registrationFailureMessages);
    }

    private void UnregisterHotkeysCore()
    {
        foreach (var registeredHotkey in _registeredHotkeys)
            _ = Win32.UnregisterHotKey(0, registeredHotkey.Identifier);

        _registeredHotkeys.Clear();
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
            RegistrationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly record struct RegisteredHotkey(int Identifier, HotkeyActionType HotkeyActionType, uint NativeModifierMask, uint NativeVirtualKey);
}
