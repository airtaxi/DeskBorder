namespace DeskBorder.Services;

public interface IFileLogService
{
    bool HasLogs();

    Task ExportAsync(string destinationFilePath);

    void WriteInformation(string source, string message);

    void WriteWarning(string source, string message);

    void WriteWarning(string source, string message, Exception exception);

    void WriteError(string source, string message);

    void WriteError(string source, string message, Exception exception);
}
