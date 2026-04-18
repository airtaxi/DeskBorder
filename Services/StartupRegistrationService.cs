using Windows.ApplicationModel;

namespace DeskBorder.Services;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string StartupTaskIdentifier = "DeskBorderStartup";

    public async Task<bool> GetIsEnabledAsync()
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskIdentifier);
            return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
    }

    public async Task SetIsEnabledAsync(bool isEnabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskIdentifier);
            if (isEnabled)
                _ = await startupTask.RequestEnableAsync();
            else
                startupTask.Disable();
        }
        catch
        {
        }
    }
}
