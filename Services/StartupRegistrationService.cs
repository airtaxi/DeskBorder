using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class StartupRegistrationService(IFileLogService fileLogService) : IStartupRegistrationService
{
    private readonly IFileLogService _fileLogService = fileLogService;

    public bool IsCurrentProcessElevated => StartupRegistrationHelper.IsCurrentProcessElevated();

    public async Task<StartupRegistrationState> GetStateAsync()
    {
        try
        {
            return await StartupRegistrationHelper.GetStartupRegistrationStateAsync();
        }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(nameof(StartupRegistrationService), "Failed to read startup registration state.", exception);
            return default;
        }
    }

    public async Task SetStateAsync(StartupRegistrationState startupRegistrationState)
    {
        try
        {
            await StartupRegistrationHelper.SetStartupRegistrationStateAsync(
                startupRegistrationState.IsLaunchOnStartupEnabled,
                startupRegistrationState.IsAlwaysRunAsAdministratorEnabled);
            _fileLogService.WriteInformation(
                nameof(StartupRegistrationService),
                $"Applied startup registration state. LaunchOnStartup={startupRegistrationState.IsLaunchOnStartupEnabled}, AlwaysRunAsAdministrator={startupRegistrationState.IsAlwaysRunAsAdministratorEnabled}.");
        }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(
                nameof(StartupRegistrationService),
                $"Failed to set startup registration state. LaunchOnStartup={startupRegistrationState.IsLaunchOnStartupEnabled}, AlwaysRunAsAdministrator={startupRegistrationState.IsAlwaysRunAsAdministratorEnabled}.",
                exception);
        }
    }
}
