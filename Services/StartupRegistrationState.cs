namespace DeskBorder.Services;

public readonly record struct StartupRegistrationState
{
    public required bool IsLaunchOnStartupEnabled { get; init; }

    public required bool IsAlwaysRunAsAdministratorEnabled { get; init; }
}
