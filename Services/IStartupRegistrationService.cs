namespace DeskBorder.Services;

public interface IStartupRegistrationService
{
    bool IsCurrentProcessElevated { get; }

    Task<StartupRegistrationState> GetStateAsync();

    Task SetStateAsync(StartupRegistrationState startupRegistrationState);
}
