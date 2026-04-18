namespace DeskBorder.Services;

public interface IStoreUpdateService
{
    void Initialize();

    Task<int> GetAvailableUpdateCountAsync();

    Task<bool> OpenStoreProductPageAsync();
}
