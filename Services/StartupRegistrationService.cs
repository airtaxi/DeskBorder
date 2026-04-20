using Windows.ApplicationModel;

namespace DeskBorder.Services;

public sealed class StartupRegistrationService(IFileLogService fileLogService) : IStartupRegistrationService
{
    private const string StartupTaskIdentifier = "DeskBorderStartup";
    private readonly IFileLogService _fileLogService = fileLogService;

    public async Task<bool> GetIsEnabledAsync()
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskIdentifier);
            return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(nameof(StartupRegistrationService), "Failed to read startup registration state.", exception);
            return false;
        }
    }

    public async Task SetIsEnabledAsync(bool isEnabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskIdentifier);
            if (isEnabled)
            {
                var startupTaskState = await startupTask.RequestEnableAsync();
                _fileLogService.WriteInformation(nameof(StartupRegistrationService), $"Requested startup registration enable. Result={startupTaskState}.");
            }
            else
            {
                startupTask.Disable();
                _fileLogService.WriteInformation(nameof(StartupRegistrationService), "Disabled startup registration.");
            }
        }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(nameof(StartupRegistrationService), $"Failed to set startup registration state to {isEnabled}.", exception);
        }
    }
}
