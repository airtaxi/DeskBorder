using DeskBorder.Interop;
using DeskBorder.Helpers;
using DeskBorder.Interop.VirtualDesktop;
using DeskBorder.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskBorder.Services;

public sealed class VirtualDesktopService(ISettingsService settingsService) : IVirtualDesktopService
{
    private const int WindowActivationRetryCount = 10;
    private const int WindowActivationRetryDelayInMilliseconds = 50;
    private readonly ISettingsService _settingsService = settingsService;

    public DesktopNavigationResult CreateDesktopAndSwitch(DesktopSwitchDirection desktopSwitchDirection)
    {
        if (desktopSwitchDirection != DesktopSwitchDirection.Next)
        {
            var workspaceSnapshot = GetWorkspaceSnapshot();
            return new()
            {
                OperationStatus = VirtualDesktopOperationStatus.UnsupportedDirection,
                PreviousWorkspaceSnapshot = workspaceSnapshot,
                CurrentWorkspaceSnapshot = workspaceSnapshot
            };
        }

        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var previousWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        var createdVirtualDesktop = VirtualDesktopFoundation.CreateDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal);
        VirtualDesktopFoundation.SwitchDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, createdVirtualDesktop);
        var currentWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        return new()
        {
            OperationStatus = VirtualDesktopOperationStatus.Success,
            NavigationActionKind = DesktopNavigationActionKind.CreatedAndSwitched,
            PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
            CurrentWorkspaceSnapshot = currentWorkspaceSnapshot,
            SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier,
            TargetDesktopIdentifier = currentWorkspaceSnapshot.CurrentDesktopIdentifier
        };
    }

    public DesktopDeletionResult DeleteDesktop(string desktopIdentifier, string fallbackDesktopIdentifier)
    {
        if (!TryParseDesktopIdentifier(desktopIdentifier, out var parsedDesktopIdentifier))
            return CreateFailedDeletionResult(VirtualDesktopOperationStatus.InvalidDesktopIdentifier);

        if (!TryParseDesktopIdentifier(fallbackDesktopIdentifier, out var parsedFallbackDesktopIdentifier))
            return CreateFailedDeletionResult(VirtualDesktopOperationStatus.InvalidDesktopIdentifier);

        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var previousWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        var removeVirtualDesktop = TryFindVirtualDesktop(virtualDesktopShellConnection.VirtualDesktopShell, parsedDesktopIdentifier);
        if (removeVirtualDesktop is null)
            return CreateFailedDeletionResult(VirtualDesktopOperationStatus.DesktopNotFound, previousWorkspaceSnapshot);

        var fallbackVirtualDesktop = TryFindVirtualDesktop(virtualDesktopShellConnection.VirtualDesktopShell, parsedFallbackDesktopIdentifier);
        if (fallbackVirtualDesktop is null)
            return CreateFailedDeletionResult(VirtualDesktopOperationStatus.DesktopNotFound, previousWorkspaceSnapshot);

        VirtualDesktopFoundation.RemoveDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, removeVirtualDesktop, fallbackVirtualDesktop);
        return new()
        {
            OperationStatus = VirtualDesktopOperationStatus.Success,
            PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
            CurrentWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell),
            DeletedDesktopIdentifier = desktopIdentifier,
            FallbackDesktopIdentifier = fallbackDesktopIdentifier
        };
    }

    public DesktopAutoDeletionValidationResult EvaluateAutoDeletion(string sourceDesktopIdentifier, string targetDesktopIdentifier)
    {
        var currentSettings = _settingsService.Settings;
        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var workspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        if (!currentSettings.IsAutoDeleteEnabled)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.DisabledBySetting,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier
            };
        }

        var sourceDesktopEntry = workspaceSnapshot.DesktopEntries.FirstOrDefault(desktopEntry => string.Equals(desktopEntry.DesktopIdentifier, sourceDesktopIdentifier, StringComparison.Ordinal));
        if (sourceDesktopEntry is null)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.SourceDesktopNotFound,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier
            };
        }

        var targetDesktopEntry = workspaceSnapshot.DesktopEntries.FirstOrDefault(desktopEntry => string.Equals(desktopEntry.DesktopIdentifier, targetDesktopIdentifier, StringComparison.Ordinal));
        if (targetDesktopEntry is null)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.TargetDesktopNotFound,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier
            };
        }

        if (!sourceDesktopEntry.IsLeftOuterDesktop && !sourceDesktopEntry.IsRightOuterDesktop)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.SourceDesktopIsNotOuterDesktop,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier
            };
        }

        var isTargetDesktopAdjacentInward =
            sourceDesktopEntry.IsLeftOuterDesktop && targetDesktopEntry.DesktopNumber == sourceDesktopEntry.DesktopNumber + 1
            || sourceDesktopEntry.IsRightOuterDesktop && targetDesktopEntry.DesktopNumber == sourceDesktopEntry.DesktopNumber - 1;
        if (!isTargetDesktopAdjacentInward)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.TargetDesktopIsNotAdjacentInward,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier
            };
        }

        var desktopWindowInventory = GetDesktopWindowInventory(virtualDesktopShellConnection.VirtualDesktopShell, sourceDesktopIdentifier);
        if (desktopWindowInventory.VisibleWindowCount > 0)
        {
            return new()
            {
                ValidationStatus = DesktopAutoDeletionValidationStatus.SourceDesktopContainsWindows,
                WorkspaceSnapshot = workspaceSnapshot,
                SourceDesktopIdentifier = sourceDesktopIdentifier,
                TargetDesktopIdentifier = targetDesktopIdentifier,
                VisibleWindowCount = desktopWindowInventory.VisibleWindowCount,
                BlockingProcessNames = desktopWindowInventory.ProcessNames
            };
        }

        return new()
        {
            ValidationStatus = DesktopAutoDeletionValidationStatus.Allowed,
            WorkspaceSnapshot = workspaceSnapshot,
            SourceDesktopIdentifier = sourceDesktopIdentifier,
            TargetDesktopIdentifier = targetDesktopIdentifier
        };
    }

    public VirtualDesktopWorkspaceSnapshot GetWorkspaceSnapshot()
    {
        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        return CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
    }

    public DesktopNavigationResult MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection desktopSwitchDirection)
    {
        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var previousWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        var focusedWindowHandle = Win32.GetForegroundWindow();
        if (focusedWindowHandle == 0)
            return CreateFailedNavigationResult(VirtualDesktopOperationStatus.WindowNotFound, previousWorkspaceSnapshot);

        if (!VirtualDesktopFoundation.TryGetApplicationView(virtualDesktopShellConnection.VirtualDesktopShell.ApplicationViewCollection, focusedWindowHandle, out var applicationView))
            return CreateFailedNavigationResult(VirtualDesktopOperationStatus.WindowNotFound, previousWorkspaceSnapshot);

        ArgumentNullException.ThrowIfNull(applicationView);
        if (!VirtualDesktopFoundation.CanMoveViewBetweenDesktops(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, applicationView))
            return CreateFailedNavigationResult(VirtualDesktopOperationStatus.WindowCannotMove, previousWorkspaceSnapshot);

        if (!VirtualDesktopFoundation.TryGetWindowDesktopIdentifier(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManager, focusedWindowHandle, out var currentDesktopIdentifier))
            return CreateFailedNavigationResult(VirtualDesktopOperationStatus.WindowNotFound, previousWorkspaceSnapshot);

        if (!VirtualDesktopFoundation.TryFindDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, currentDesktopIdentifier, out var currentVirtualDesktop))
            return CreateFailedNavigationResult(VirtualDesktopOperationStatus.DesktopNotFound, previousWorkspaceSnapshot);

        ArgumentNullException.ThrowIfNull(currentVirtualDesktop);
        if (!VirtualDesktopFoundation.TryGetAdjacentDesktop(
            virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal,
            currentVirtualDesktop,
            ConvertToAdjacentVirtualDesktopDirection(desktopSwitchDirection),
            out var adjacentVirtualDesktop))
        {
            return new()
            {
                OperationStatus = VirtualDesktopOperationStatus.NoAdjacentDesktop,
                PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
                CurrentWorkspaceSnapshot = previousWorkspaceSnapshot,
                SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier
            };
        }

        ArgumentNullException.ThrowIfNull(adjacentVirtualDesktop);
        var targetDesktopIdentifier = VirtualDesktopFoundation.GetDesktopIdentifier(adjacentVirtualDesktop);
        VirtualDesktopFoundation.MoveViewToDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, applicationView, adjacentVirtualDesktop);
        VirtualDesktopFoundation.SwitchDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, adjacentVirtualDesktop);
        FocusWindowOnTargetDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManager, focusedWindowHandle, targetDesktopIdentifier);
        return new()
        {
            OperationStatus = VirtualDesktopOperationStatus.Success,
            NavigationActionKind = DesktopNavigationActionKind.Switched,
            PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
            CurrentWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell),
            SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier,
            TargetDesktopIdentifier = targetDesktopIdentifier.ToString()
        };
    }

    public DesktopNavigationResult SwitchDesktop(DesktopSwitchDirection desktopSwitchDirection)
    {
        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var previousWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        var currentVirtualDesktop = VirtualDesktopFoundation.GetCurrentDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal);
        if (!VirtualDesktopFoundation.TryGetAdjacentDesktop(
            virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal,
            currentVirtualDesktop,
            ConvertToAdjacentVirtualDesktopDirection(desktopSwitchDirection),
            out var adjacentVirtualDesktop))
        {
            return new()
            {
                OperationStatus = VirtualDesktopOperationStatus.NoAdjacentDesktop,
                PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
                CurrentWorkspaceSnapshot = previousWorkspaceSnapshot,
                SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier
            };
        }

        ArgumentNullException.ThrowIfNull(adjacentVirtualDesktop);
        var targetDesktopIdentifier = VirtualDesktopFoundation.GetDesktopIdentifier(adjacentVirtualDesktop).ToString();
        VirtualDesktopFoundation.SwitchDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, adjacentVirtualDesktop);
        return new()
        {
            OperationStatus = VirtualDesktopOperationStatus.Success,
            NavigationActionKind = DesktopNavigationActionKind.Switched,
            PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
            CurrentWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell),
            SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier,
            TargetDesktopIdentifier = targetDesktopIdentifier
        };
    }

    public DesktopNavigationResult SwitchToDesktop(string desktopIdentifier)
    {
        if (!TryParseDesktopIdentifier(desktopIdentifier, out var parsedDesktopIdentifier))
        {
            var workspaceSnapshot = GetWorkspaceSnapshot();
            return new()
            {
                OperationStatus = VirtualDesktopOperationStatus.InvalidDesktopIdentifier,
                PreviousWorkspaceSnapshot = workspaceSnapshot,
                CurrentWorkspaceSnapshot = workspaceSnapshot
            };
        }

        using var virtualDesktopShellConnection = CreateVirtualDesktopShellConnection();
        var previousWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell);
        var targetVirtualDesktop = TryFindVirtualDesktop(virtualDesktopShellConnection.VirtualDesktopShell, parsedDesktopIdentifier);
        if (targetVirtualDesktop is null)
        {
            return new()
            {
                OperationStatus = VirtualDesktopOperationStatus.DesktopNotFound,
                PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
                CurrentWorkspaceSnapshot = previousWorkspaceSnapshot
            };
        }

        VirtualDesktopFoundation.SwitchDesktop(virtualDesktopShellConnection.VirtualDesktopShell.VirtualDesktopManagerInternal, targetVirtualDesktop);
        return new()
        {
            OperationStatus = VirtualDesktopOperationStatus.Success,
            NavigationActionKind = DesktopNavigationActionKind.SwitchedToSelectedDesktop,
            PreviousWorkspaceSnapshot = previousWorkspaceSnapshot,
            CurrentWorkspaceSnapshot = CreateWorkspaceSnapshot(virtualDesktopShellConnection.VirtualDesktopShell),
            SourceDesktopIdentifier = previousWorkspaceSnapshot.CurrentDesktopIdentifier,
            TargetDesktopIdentifier = desktopIdentifier
        };
    }

    private static AdjacentVirtualDesktopDirection ConvertToAdjacentVirtualDesktopDirection(DesktopSwitchDirection desktopSwitchDirection) => desktopSwitchDirection switch
    {
        DesktopSwitchDirection.Previous => AdjacentVirtualDesktopDirection.Left,
        DesktopSwitchDirection.Next => AdjacentVirtualDesktopDirection.Right,
        _ => throw new InvalidOperationException("The requested desktop switch direction is not supported.")
    };

    private static bool AttachThreadInputIfNeeded(uint currentThreadIdentifier, uint targetThreadIdentifier)
        => targetThreadIdentifier != 0
        && targetThreadIdentifier != currentThreadIdentifier
        && Win32.AttachThreadInput(currentThreadIdentifier, targetThreadIdentifier, true);

    private static DesktopDeletionResult CreateFailedDeletionResult(VirtualDesktopOperationStatus virtualDesktopOperationStatus, VirtualDesktopWorkspaceSnapshot? workspaceSnapshot = null) => new() { OperationStatus = virtualDesktopOperationStatus, PreviousWorkspaceSnapshot = workspaceSnapshot ?? new(), CurrentWorkspaceSnapshot = workspaceSnapshot ?? new() };

    private static DesktopNavigationResult CreateFailedNavigationResult(VirtualDesktopOperationStatus virtualDesktopOperationStatus, VirtualDesktopWorkspaceSnapshot workspaceSnapshot) => new()
    {
        OperationStatus = virtualDesktopOperationStatus,
        PreviousWorkspaceSnapshot = workspaceSnapshot,
        CurrentWorkspaceSnapshot = workspaceSnapshot,
        SourceDesktopIdentifier = workspaceSnapshot.CurrentDesktopIdentifier
    };

    private static VirtualDesktopWorkspaceSnapshot CreateWorkspaceSnapshot(VirtualDesktopShell virtualDesktopShell)
    {
        var virtualDesktops = VirtualDesktopFoundation.GetDesktops(virtualDesktopShell.VirtualDesktopManagerInternal);
        var currentDesktopIdentifier = VirtualDesktopFoundation.GetDesktopIdentifier(VirtualDesktopFoundation.GetCurrentDesktop(virtualDesktopShell.VirtualDesktopManagerInternal)).ToString();
        var desktopEntries = virtualDesktops
            .Select((virtualDesktop, index) =>
            {
                var desktopIdentifier = VirtualDesktopFoundation.GetDesktopIdentifier(virtualDesktop).ToString();
                var desktopNumber = index + 1;
                return new VirtualDesktopEntry
                {
                    DesktopIdentifier = desktopIdentifier,
                    DesktopNumber = desktopNumber,
                    DisplayName = SettingsDisplayFormatter.FormatDesktopDisplayName(desktopNumber),
                    IsCurrentDesktop = string.Equals(desktopIdentifier, currentDesktopIdentifier, StringComparison.Ordinal),
                    IsLeftOuterDesktop = index == 0,
                    IsRightOuterDesktop = index == virtualDesktops.Count - 1
                };
            })
            .ToArray();
        var currentDesktopEntry = desktopEntries.FirstOrDefault(desktopEntry => desktopEntry.IsCurrentDesktop) ?? desktopEntries.FirstOrDefault();
        return new()
        {
            DesktopEntries = desktopEntries,
            CurrentDesktopIdentifier = currentDesktopEntry?.DesktopIdentifier ?? string.Empty,
            CurrentDesktopNumber = currentDesktopEntry?.DesktopNumber ?? 0
        };
    }

    private static void FocusWindowOnTargetDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, VirtualDesktopIdentifier targetDesktopIdentifier)
    {
        for (var retryIndex = 0; retryIndex < WindowActivationRetryCount; retryIndex++)
        {
            if (!VirtualDesktopFoundation.TryGetWindowDesktopIdentifier(virtualDesktopManager, windowHandle, out var currentDesktopIdentifier))
                return;

            if (!VirtualDesktopFoundation.TryIsWindowOnCurrentDesktop(virtualDesktopManager, windowHandle, out var isWindowOnCurrentDesktop))
                return;

            if (currentDesktopIdentifier != targetDesktopIdentifier
                || !isWindowOnCurrentDesktop)
            {
                Thread.Sleep(WindowActivationRetryDelayInMilliseconds);
                continue;
            }

            TryActivateWindow(windowHandle);
            if (Win32.GetForegroundWindow() == windowHandle)
                return;

            Thread.Sleep(WindowActivationRetryDelayInMilliseconds);
        }
    }

    private static DesktopWindowInventory GetDesktopWindowInventory(VirtualDesktopShell virtualDesktopShell, string desktopIdentifier)
    {
        if (!TryParseDesktopIdentifier(desktopIdentifier, out var parsedDesktopIdentifier))
            return new(0, []);

        var shellWindowHandle = Win32.GetShellWindow();
        var blockedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visibleWindowCount = 0;
        _ = Win32.EnumWindows((windowHandle, applicationData) =>
        {
            _ = applicationData;
            if (windowHandle == 0 || windowHandle == shellWindowHandle || !Win32.IsWindowVisible(windowHandle) || Win32.IsIconic(windowHandle))
                return true;

            if (!TryGetWindowDesktopIdentifier(virtualDesktopShell, windowHandle, out var windowDesktopIdentifier))
                return true;

            if (windowDesktopIdentifier != parsedDesktopIdentifier)
                return true;

            var processName = TryGetProcessName(windowHandle);
            if (string.IsNullOrWhiteSpace(processName))
                return true;

            visibleWindowCount++;
            _ = blockedProcessNames.Add(processName);
            return true;
        }, 0);
        return new(visibleWindowCount, [.. blockedProcessNames.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private static VirtualDesktopShellConnection CreateVirtualDesktopShellConnection() => new(VirtualDesktopFoundation.Connect());

    private static string? TryGetProcessName(nint windowHandle)
    {
        _ = Win32.GetWindowThreadProcessId(windowHandle, out var processIdentifier);
        if (processIdentifier == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processIdentifier);
            return process.ProcessName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static void TryActivateWindow(nint windowHandle)
    {
        if (Win32.IsIconic(windowHandle))
            _ = Win32.ShowWindowAsync(windowHandle, Win32.ShowWindowRestore);

        var currentThreadIdentifier = Win32.GetCurrentThreadId();
        var foregroundWindowHandle = Win32.GetForegroundWindow();
        var foregroundThreadIdentifier = foregroundWindowHandle == 0 ? 0 : Win32.GetWindowThreadProcessId(foregroundWindowHandle, out _);
        var targetWindowThreadIdentifier = Win32.GetWindowThreadProcessId(windowHandle, out _);
        var isForegroundThreadAttached = AttachThreadInputIfNeeded(currentThreadIdentifier, foregroundThreadIdentifier);
        var isTargetThreadAttached = targetWindowThreadIdentifier != foregroundThreadIdentifier
            && AttachThreadInputIfNeeded(currentThreadIdentifier, targetWindowThreadIdentifier);
        try
        {
            _ = Win32.BringWindowToTop(windowHandle);
            _ = Win32.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (isTargetThreadAttached)
                _ = Win32.AttachThreadInput(currentThreadIdentifier, targetWindowThreadIdentifier, false);

            if (isForegroundThreadAttached)
                _ = Win32.AttachThreadInput(currentThreadIdentifier, foregroundThreadIdentifier, false);
        }
    }

    private static bool TryGetWindowDesktopIdentifier(VirtualDesktopShell virtualDesktopShell, nint windowHandle, out VirtualDesktopIdentifier desktopIdentifier)
    {
        return VirtualDesktopFoundation.TryGetWindowDesktopIdentifier(virtualDesktopShell.VirtualDesktopManager, windowHandle, out desktopIdentifier);
    }

    private static bool TryParseDesktopIdentifier(string desktopIdentifier, out VirtualDesktopIdentifier parsedDesktopIdentifier)
    {
        if (Guid.TryParse(desktopIdentifier, out var parsedGuid)) { parsedDesktopIdentifier = parsedGuid; return true; }

        parsedDesktopIdentifier = default;
        return false;
    }

    private static IVirtualDesktop? TryFindVirtualDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopIdentifier desktopIdentifier)
    {
        return VirtualDesktopFoundation.TryFindDesktop(virtualDesktopShell.VirtualDesktopManagerInternal, desktopIdentifier, out var virtualDesktop)
            ? virtualDesktop
            : null;
    }

    private sealed class VirtualDesktopShellConnection(VirtualDesktopShell virtualDesktopShell) : IDisposable
    {
        public VirtualDesktopShell VirtualDesktopShell { get; } = virtualDesktopShell;

        public void Dispose() => _ = VirtualDesktopShell;
    }

    private readonly record struct DesktopWindowInventory(int VisibleWindowCount, string[] ProcessNames);
}
