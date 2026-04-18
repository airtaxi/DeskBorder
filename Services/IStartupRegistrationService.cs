namespace DeskBorder.Services;

public interface IStartupRegistrationService
{
    Task<bool> GetIsEnabledAsync();

    Task SetIsEnabledAsync(bool isEnabled);
}
