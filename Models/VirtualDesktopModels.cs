namespace DeskBorder.Models;

public enum DesktopSwitchDirection
{
    Previous,
    Next,
}

public enum VirtualDesktopOperationStatus
{
    Success,
    NoAdjacentDesktop,
    InvalidDesktopIdentifier,
    DesktopNotFound,
    WindowNotFound,
    WindowCannotMove,
    UnsupportedDirection,
    UnexpectedError,
}

public enum DesktopNavigationActionKind
{
    None,
    Switched,
    CreatedAndSwitched,
    SwitchedToSelectedDesktop,
}

public enum DesktopAutoDeletionValidationStatus
{
    Allowed,
    DisabledBySetting,
    SourceDesktopNotFound,
    TargetDesktopNotFound,
    SourceDesktopIsNotOuterDesktop,
    TargetDesktopIsNotAdjacentInward,
    SourceDesktopContainsWindows,
}

public sealed record VirtualDesktopEntry
{
    public required string DesktopIdentifier { get; init; }

    public required int DesktopNumber { get; init; }

    public required string DisplayName { get; init; }

    public bool IsCurrentDesktop { get; init; }

    public bool IsLeftOuterDesktop { get; init; }

    public bool IsRightOuterDesktop { get; init; }
}

public sealed record VirtualDesktopWorkspaceSnapshot
{
    public VirtualDesktopEntry[] DesktopEntries { get; init; } = [];

    public string CurrentDesktopIdentifier { get; init; } = string.Empty;

    public int CurrentDesktopNumber { get; init; }

    public int DesktopCount => DesktopEntries.Length;
}

public sealed record DesktopNavigationResult
{
    public VirtualDesktopOperationStatus OperationStatus { get; init; } = VirtualDesktopOperationStatus.Success;

    public DesktopNavigationActionKind NavigationActionKind { get; init; } = DesktopNavigationActionKind.None;

    public VirtualDesktopWorkspaceSnapshot PreviousWorkspaceSnapshot { get; init; } = new();

    public VirtualDesktopWorkspaceSnapshot CurrentWorkspaceSnapshot { get; init; } = new();

    public string? SourceDesktopIdentifier { get; init; }

    public string? TargetDesktopIdentifier { get; init; }

    public bool IsSuccessful => OperationStatus == VirtualDesktopOperationStatus.Success;
}

public sealed record DesktopAutoDeletionValidationResult
{
    public DesktopAutoDeletionValidationStatus ValidationStatus { get; init; } = DesktopAutoDeletionValidationStatus.DisabledBySetting;

    public VirtualDesktopWorkspaceSnapshot WorkspaceSnapshot { get; init; } = new();

    public string? SourceDesktopIdentifier { get; init; }

    public string? TargetDesktopIdentifier { get; init; }

    public int VisibleWindowCount { get; init; }

    public string[] BlockingProcessNames { get; init; } = [];

    public bool CanAutoDelete => ValidationStatus == DesktopAutoDeletionValidationStatus.Allowed;
}

public sealed record DesktopDeletionResult
{
    public VirtualDesktopOperationStatus OperationStatus { get; init; } = VirtualDesktopOperationStatus.Success;

    public VirtualDesktopWorkspaceSnapshot PreviousWorkspaceSnapshot { get; init; } = new();

    public VirtualDesktopWorkspaceSnapshot CurrentWorkspaceSnapshot { get; init; } = new();

    public string? DeletedDesktopIdentifier { get; init; }

    public string? FallbackDesktopIdentifier { get; init; }

    public bool IsSuccessful => OperationStatus == VirtualDesktopOperationStatus.Success;
}

public sealed record PendingDesktopDeletion
{
    public required string DesktopIdentifier { get; init; }

    public required string DesktopDisplayName { get; init; }

    public required string FallbackDesktopIdentifier { get; init; }

    public required TimeSpan UndoDuration { get; init; }
}
