using DeskBorder.Models;
using DeskBorder.Services;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace DeskBorder.Helpers;

public static class StartupRegistrationHelper
{
    public const string AdministratorTaskLaunchCommandLineArgument = "--administrator-task";
    public const string StartupLaunchCommandLineArgument = "--startup";

    private const string ActivationProtocolName = "deskborder";
    private const string AdministratorTaskActivationToken = "administrator-task";
    private const string SettingsKey = "DeskBorderSettings";
    private const string AdministratorScheduledTaskName = "DeskBorder.Administrator";
    private const string HighestAvailableRunLevel = "HighestAvailable";
    private const string LeastPrivilegeRunLevel = "LeastPrivilege";
    private const string StartupActivationToken = "startup";
    private const string StartupTaskIdentifier = "DeskBorderStartup";
    private const string StartupScheduledTaskName = "DeskBorder.Startup";

    private static readonly XNamespace s_taskSchedulerNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly record struct ScheduledTaskDefinition(
        string Command,
        string? Arguments,
        string? WorkingDirectory,
        string RunLevel);

    public static async Task<StartupRegistrationState> GetStartupRegistrationStateAsync() => new()
    {
        IsLaunchOnStartupEnabled = await TryGetScheduledTaskDefinitionAsync(StartupScheduledTaskName) is not null || await IsStartupTaskEnabledAsync(),
        IsAlwaysRunAsAdministratorEnabled = await TryGetScheduledTaskDefinitionAsync(AdministratorScheduledTaskName) is not null
    };

    public static bool IsCurrentProcessElevated()
    {
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(windowsIdentity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsStartupActivation(string commandLineArguments) => string.Equals(ParseLaunchActivationToken(commandLineArguments), StartupActivationToken, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldActivateManageWindow(AppActivationArguments appActivationArguments) => appActivationArguments.Kind != ExtendedActivationKind.StartupTask
        && !string.Equals(GetActivationToken(appActivationArguments), StartupActivationToken, StringComparison.OrdinalIgnoreCase);

    public static DeskBorderSettings? TryLoadStoredSettings()
    {
        if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is not string serializedSettings)
            return null;

        try { return JsonSerializer.Deserialize(serializedSettings, DeskBorderSettingsSerializationContext.Default.DeskBorderSettings); }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static async Task<bool> TryLaunchAsAdministratorAsync()
    {
        var scheduledTaskCommandResult = await RunScheduledTaskCommandAsync([ "/Run", "/TN", AdministratorScheduledTaskName ]);
        return scheduledTaskCommandResult.ExitCode == 0;
    }

    public static bool ShouldTryLaunchAsAdministratorFromStoredSettings(AppActivationArguments appActivationArguments)
    {
        if (IsCurrentProcessElevated() || string.Equals(GetActivationToken(appActivationArguments), AdministratorTaskActivationToken, StringComparison.OrdinalIgnoreCase))
            return false;

        return TryLoadStoredSettings()?.IsAlwaysRunAsAdministratorEnabled == true;
    }

    public static async Task SetStartupRegistrationStateAsync(bool isLaunchOnStartupEnabled, bool isAlwaysRunAsAdministratorEnabled)
    {
        var startupTaskDefinition = await TryGetScheduledTaskDefinitionAsync(StartupScheduledTaskName);
        var administratorTaskDefinition = await TryGetScheduledTaskDefinitionAsync(AdministratorScheduledTaskName);

        if (isLaunchOnStartupEnabled)
        {
            if (isAlwaysRunAsAdministratorEnabled)
            {
                var expectedStartupTaskDefinition = CreateExpectedScheduledTaskDefinition(StartupActivationToken, HighestAvailableRunLevel);
                if (!AreScheduledTaskDefinitionsEquivalent(startupTaskDefinition, expectedStartupTaskDefinition))
                    await RegisterScheduledTaskAsync(StartupScheduledTaskName, CreateScheduledTaskXml(expectedStartupTaskDefinition, hasLogonTrigger: true));

                await SetStartupTaskEnabledAsync(false);
            }
            else
            {
                if (startupTaskDefinition is not null)
                    await DeleteScheduledTaskAsync(StartupScheduledTaskName);

                await SetStartupTaskEnabledAsync(true);
            }
        }
        else
        {
            if (startupTaskDefinition is not null)
                await DeleteScheduledTaskAsync(StartupScheduledTaskName);

            await SetStartupTaskEnabledAsync(false);
        }

        if (isAlwaysRunAsAdministratorEnabled)
        {
            var expectedAdministratorTaskDefinition = CreateExpectedScheduledTaskDefinition(AdministratorTaskActivationToken, HighestAvailableRunLevel);
            if (!AreScheduledTaskDefinitionsEquivalent(administratorTaskDefinition, expectedAdministratorTaskDefinition))
                await RegisterScheduledTaskAsync(AdministratorScheduledTaskName, CreateScheduledTaskXml(expectedAdministratorTaskDefinition, hasLogonTrigger: false));
        }
        else if (administratorTaskDefinition is not null)
            await DeleteScheduledTaskAsync(AdministratorScheduledTaskName);
    }

    private static bool AreScheduledTaskDefinitionsEquivalent(ScheduledTaskDefinition? scheduledTaskDefinition, ScheduledTaskDefinition expectedScheduledTaskDefinition)
    {
        if (scheduledTaskDefinition is null)
            return false;

        return string.Equals(NormalizePath(scheduledTaskDefinition.Value.Command), NormalizePath(expectedScheduledTaskDefinition.Command), StringComparison.OrdinalIgnoreCase)
            && string.Equals(scheduledTaskDefinition.Value.Arguments?.Trim(), expectedScheduledTaskDefinition.Arguments?.Trim(), StringComparison.Ordinal)
            && string.Equals(NormalizePath(scheduledTaskDefinition.Value.WorkingDirectory), NormalizePath(expectedScheduledTaskDefinition.WorkingDirectory), StringComparison.OrdinalIgnoreCase)
            && string.Equals(scheduledTaskDefinition.Value.RunLevel, expectedScheduledTaskDefinition.RunLevel, StringComparison.OrdinalIgnoreCase);
    }

    private static ScheduledTaskDefinition CreateExpectedScheduledTaskDefinition(string activationToken, string runLevel)
    {
        var commandProcessorPath = GetCommandProcessorPath();
        return new(
            commandProcessorPath,
            CreateCommandProcessorArguments(activationToken),
            Path.GetDirectoryName(commandProcessorPath),
            runLevel);
    }

    private static string CreateScheduledTaskXml(ScheduledTaskDefinition scheduledTaskDefinition, bool hasLogonTrigger)
    {
        var currentUserSecurityIdentifier = GetCurrentUserSecurityIdentifier();
        var taskDocument = new XDocument(
            new XDeclaration("1.0", "utf-16", "yes"),
            new XElement(
                s_taskSchedulerNamespace + "Task",
                new XAttribute("version", "1.4"),
                new XElement(
                    s_taskSchedulerNamespace + "RegistrationInfo",
                    new XElement(
                        s_taskSchedulerNamespace + "Description",
                        hasLogonTrigger
                            ? "Launch DeskBorder when the current user signs in."
                            : "Launch DeskBorder on demand with administrator privileges.")),
                hasLogonTrigger
                    ? new XElement(
                        s_taskSchedulerNamespace + "Triggers",
                        new XElement(
                            s_taskSchedulerNamespace + "LogonTrigger",
                            new XElement(s_taskSchedulerNamespace + "Enabled", "true"),
                            new XElement(s_taskSchedulerNamespace + "UserId", currentUserSecurityIdentifier)))
                    : null,
                new XElement(
                    s_taskSchedulerNamespace + "Principals",
                    new XElement(
                        s_taskSchedulerNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(s_taskSchedulerNamespace + "UserId", currentUserSecurityIdentifier),
                        new XElement(s_taskSchedulerNamespace + "LogonType", "InteractiveToken"),
                        new XElement(s_taskSchedulerNamespace + "RunLevel", scheduledTaskDefinition.RunLevel))),
                new XElement(
                    s_taskSchedulerNamespace + "Settings",
                    new XElement(s_taskSchedulerNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(s_taskSchedulerNamespace + "DisallowStartIfOnBatteries", "false"),
                    new XElement(s_taskSchedulerNamespace + "StopIfGoingOnBatteries", "false"),
                    new XElement(s_taskSchedulerNamespace + "AllowHardTerminate", "true"),
                    new XElement(s_taskSchedulerNamespace + "AllowStartOnDemand", "true"),
                    new XElement(s_taskSchedulerNamespace + "StartWhenAvailable", "true"),
                    new XElement(s_taskSchedulerNamespace + "ExecutionTimeLimit", "PT0S"),
                    new XElement(s_taskSchedulerNamespace + "Priority", 7)),
                new XElement(
                    s_taskSchedulerNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(
                        s_taskSchedulerNamespace + "Exec",
                        new XElement(s_taskSchedulerNamespace + "Command", scheduledTaskDefinition.Command),
                        string.IsNullOrWhiteSpace(scheduledTaskDefinition.Arguments)
                            ? null
                            : new XElement(s_taskSchedulerNamespace + "Arguments", scheduledTaskDefinition.Arguments),
                        string.IsNullOrWhiteSpace(scheduledTaskDefinition.WorkingDirectory)
                            ? null
                            : new XElement(s_taskSchedulerNamespace + "WorkingDirectory", scheduledTaskDefinition.WorkingDirectory)))));

        using var stringWriter = new Utf16StringWriter();
        taskDocument.Save(stringWriter);
        return stringWriter.ToString();
    }

    private static async Task DeleteScheduledTaskAsync(string scheduledTaskName)
    {
        var scheduledTaskCommandResult = await RunScheduledTaskCommandAsync([ "/Delete", "/TN", scheduledTaskName, "/F" ]);
        if (scheduledTaskCommandResult.ExitCode != 0)
            throw new InvalidOperationException(CreateScheduledTaskCommandFailureMessage("delete", scheduledTaskName, scheduledTaskCommandResult));
    }

    private static string CreateScheduledTaskCommandFailureMessage(string operationName, string scheduledTaskName, ScheduledTaskCommandResult scheduledTaskCommandResult)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append("Failed to ");
        stringBuilder.Append(operationName);
        stringBuilder.Append(" the scheduled task '");
        stringBuilder.Append(scheduledTaskName);
        stringBuilder.Append("'. ExitCode=");
        stringBuilder.Append(scheduledTaskCommandResult.ExitCode);
        if (!string.IsNullOrWhiteSpace(scheduledTaskCommandResult.Output))
        {
            stringBuilder.Append(", Output=");
            stringBuilder.Append(scheduledTaskCommandResult.Output.Trim());
        }

        if (!string.IsNullOrWhiteSpace(scheduledTaskCommandResult.Error))
        {
            stringBuilder.Append(", Error=");
            stringBuilder.Append(scheduledTaskCommandResult.Error.Trim());
        }

        return stringBuilder.ToString();
    }

    private static string GetCurrentUserSecurityIdentifier() => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("The current user security identifier could not be resolved.");

    private static string? GetActivationToken(AppActivationArguments appActivationArguments)
    {
        return appActivationArguments.Kind switch
        {
            ExtendedActivationKind.Launch when appActivationArguments.Data is ILaunchActivatedEventArgs launchActivatedEventArgs => ParseLaunchActivationToken(launchActivatedEventArgs.Arguments),
            ExtendedActivationKind.Protocol when appActivationArguments.Data is IProtocolActivatedEventArgs protocolActivatedEventArgs => ParseProtocolActivationToken(protocolActivatedEventArgs.Uri),
            _ => null
        };
    }

    private static string CreateProtocolActivationUri(string activationToken) => $"{ActivationProtocolName}://{activationToken}";

    private static async Task<StartupTask> GetStartupTaskAsync() => await StartupTask.GetAsync(StartupTaskIdentifier);

    private static async Task<bool> IsStartupTaskEnabledAsync()
    {
        var startupTask = await GetStartupTaskAsync();
        return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    private static async Task SetStartupTaskEnabledAsync(bool isEnabled)
    {
        var startupTask = await GetStartupTaskAsync();
        if (isEnabled)
        {
            if (startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                return;

            _ = await startupTask.RequestEnableAsync();
            return;
        }

        if (startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            startupTask.Disable();
    }

    private static string CreateCommandProcessorArguments(string activationToken)
    {
        var protocolActivationUri = CreateProtocolActivationUri(activationToken);
        return $"/c start \"\" \"{protocolActivationUri}\"";
    }

    private static string GetCommandProcessorPath() => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static string? ParseLaunchActivationToken(string? commandLineArguments)
    {
        if (string.IsNullOrWhiteSpace(commandLineArguments))
            return null;

        var trimmedCommandLineArguments = commandLineArguments.Trim();
        if (string.Equals(trimmedCommandLineArguments, StartupLaunchCommandLineArgument, StringComparison.OrdinalIgnoreCase))
            return StartupActivationToken;

        if (string.Equals(trimmedCommandLineArguments, AdministratorTaskLaunchCommandLineArgument, StringComparison.OrdinalIgnoreCase))
            return AdministratorTaskActivationToken;

        return Uri.TryCreate(trimmedCommandLineArguments, UriKind.Absolute, out var protocolActivationUri)
            ? ParseProtocolActivationToken(protocolActivationUri)
            : null;
    }

    private static string? ParseProtocolActivationToken(Uri? protocolActivationUri)
    {
        if (protocolActivationUri is null || !string.Equals(protocolActivationUri.Scheme, ActivationProtocolName, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrWhiteSpace(protocolActivationUri.Host))
            return protocolActivationUri.Host;

        var normalizedAbsolutePath = protocolActivationUri.AbsolutePath.Trim('/');
        return string.IsNullOrWhiteSpace(normalizedAbsolutePath)
            ? null
            : normalizedAbsolutePath;
    }

    private static string? NormalizePath(string? path) => string.IsNullOrWhiteSpace(path)
        ? null
        : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()), Environment.SystemDirectory);

    private static async Task RegisterScheduledTaskAsync(string scheduledTaskName, string scheduledTaskXml)
    {
        var temporaryTaskDefinitionPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(temporaryTaskDefinitionPath, scheduledTaskXml, Encoding.Unicode);
        try
        {
            var scheduledTaskCommandResult = await RunScheduledTaskCommandAsync([ "/Create", "/TN", scheduledTaskName, "/XML", temporaryTaskDefinitionPath, "/F" ]);
            if (scheduledTaskCommandResult.ExitCode != 0)
                throw new InvalidOperationException(CreateScheduledTaskCommandFailureMessage("register", scheduledTaskName, scheduledTaskCommandResult));
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryTaskDefinitionPath))
                    File.Delete(temporaryTaskDefinitionPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<ScheduledTaskCommandResult> RunScheduledTaskCommandAsync(IReadOnlyList<string> arguments)
    {
        var processStartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            processStartInfo.ArgumentList.Add(argument);

        using var scheduledTaskProcess = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start schtasks.exe.");
        var outputTask = scheduledTaskProcess.StandardOutput.ReadToEndAsync();
        var errorTask = scheduledTaskProcess.StandardError.ReadToEndAsync();
        await scheduledTaskProcess.WaitForExitAsync();
        return new(scheduledTaskProcess.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<ScheduledTaskDefinition?> TryGetScheduledTaskDefinitionAsync(string scheduledTaskName)
    {
        var scheduledTaskCommandResult = await RunScheduledTaskCommandAsync([ "/Query", "/TN", scheduledTaskName, "/XML" ]);
        if (scheduledTaskCommandResult.ExitCode != 0)
            return null;

        var scheduledTaskDocument = XDocument.Parse(scheduledTaskCommandResult.Output);
        var execElement = scheduledTaskDocument.Descendants(s_taskSchedulerNamespace + "Exec").FirstOrDefault();
        if (execElement is null)
            return null;

        return new(
            execElement.Element(s_taskSchedulerNamespace + "Command")?.Value ?? string.Empty,
            execElement.Element(s_taskSchedulerNamespace + "Arguments")?.Value,
            execElement.Element(s_taskSchedulerNamespace + "WorkingDirectory")?.Value,
            scheduledTaskDocument.Descendants(s_taskSchedulerNamespace + "RunLevel").FirstOrDefault()?.Value ?? LeastPrivilegeRunLevel);
    }

    private sealed class Utf16StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.Unicode;
    }

    private readonly record struct ScheduledTaskCommandResult(int ExitCode, string Output, string Error);
}
