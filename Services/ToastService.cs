using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Pages.Toast;
using DeskBorder.Views;

namespace DeskBorder.Services;

public sealed class ToastService : IToastService
{
    private readonly object _syncLock = new();
    private readonly IThemeService _themeService;
    private ActiveToastContext? _activeToastContext;
    private ToastPageBase? _activeToastPage;
    private ToastWindow? _toastWindow;

    public ToastService(IThemeService themeService) => _themeService = themeService;

    public bool IsToastVisible { get; private set; }

    public Task DismissAsync() => GetActiveToastContext() is { } activeToastContext
        ? CompleteToastAsync(activeToastContext, ToastPresentationResultKind.Dismissed)
        : Task.CompletedTask;

    public async Task<ToastPresentationResult> ShowToastAsync(
        ToastPresentationOptions toastPresentationOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toastPresentationOptions);
        if (toastPresentationOptions.Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(toastPresentationOptions), "Toast duration must be zero or positive.");

        await ReplaceActiveToastAsync();

        var activeToastContext = new ActiveToastContext();
        SetActiveToastContext(activeToastContext);

        using var cancellationTokenRegistration = cancellationToken.Register(() => _ = CompleteToastAsync(activeToastContext, ToastPresentationResultKind.Dismissed));
        await UiThreadHelper.ExecuteAsync(() =>
        {
            EnsureToastWindow();
            SetToastPage(toastPresentationOptions);
            _toastWindow?.ShowToast();
            IsToastVisible = true;
        });

        try
        {
            await Task.Delay(toastPresentationOptions.Duration, activeToastContext.CancellationTokenSource.Token);
            await CompleteToastAsync(activeToastContext, ToastPresentationResultKind.TimedOut);
        }
        catch (TaskCanceledException) { }

        return await activeToastContext.TaskCompletionSource.Task;
    }

    private async Task CompleteToastAsync(ActiveToastContext activeToastContext, ToastPresentationResultKind toastPresentationResultKind)
    {
        var toastPresentationResult = new ToastPresentationResult
        {
            ResultKind = toastPresentationResultKind
        };
        if (!activeToastContext.TaskCompletionSource.TrySetResult(toastPresentationResult))
            return;

        activeToastContext.CancellationTokenSource.Cancel();
        activeToastContext.CancellationTokenSource.Dispose();

        lock (_syncLock)
        {
            if (ReferenceEquals(_activeToastContext, activeToastContext))
                _activeToastContext = null;
        }

        await UiThreadHelper.ExecuteAsync(() =>
        {
            ResetToastPage();
            _toastWindow?.HideToast();
            IsToastVisible = false;
        });
    }

    private static ToastPageBase CreateToastPage(ToastPresentationOptions toastPresentationOptions) => toastPresentationOptions switch
    {
        WarningToastPresentationOptions warningToastPresentationOptions => new WarningToastPage(warningToastPresentationOptions),
        HotkeyToastPresentationOptions hotkeyToastPresentationOptions => new HotkeyToastPage(hotkeyToastPresentationOptions),
        _ => throw new InvalidOperationException($"Unsupported toast presentation options type: {toastPresentationOptions.GetType().FullName}")
    };

    private void EnsureToastWindow()
    {
        if (_toastWindow is not null)
            return;

        _toastWindow = new ToastWindow(_themeService);
    }

    private ActiveToastContext? GetActiveToastContext()
    {
        lock (_syncLock)
            return _activeToastContext;
    }

    private void OnToastPageActionInvoked(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var activeToastContext = GetActiveToastContext();
        if (activeToastContext is null)
            return;

        _ = CompleteToastAsync(activeToastContext, ToastPresentationResultKind.ActionInvoked);
    }

    private Task ReplaceActiveToastAsync() => GetActiveToastContext() is { } activeToastContext
        ? CompleteToastAsync(activeToastContext, ToastPresentationResultKind.Replaced)
        : Task.CompletedTask;

    private void ResetToastPage()
    {
        _activeToastPage?.ActionInvoked -= OnToastPageActionInvoked;

        _activeToastPage = null;
        _toastWindow?.ClearToastPage();
    }

    private void SetActiveToastContext(ActiveToastContext activeToastContext)
    {
        lock (_syncLock)
        {
            _activeToastContext = activeToastContext;
        }
    }

    private void SetToastPage(ToastPresentationOptions toastPresentationOptions)
    {
        ResetToastPage();
        var toastPage = CreateToastPage(toastPresentationOptions);

        toastPage.ActionInvoked += OnToastPageActionInvoked;
        _activeToastPage = toastPage;
        _toastWindow!.SetToastPage(toastPage, toastPresentationOptions.WindowWidth, toastPresentationOptions.WindowHeight);
    }

    private sealed class ActiveToastContext
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public TaskCompletionSource<ToastPresentationResult> TaskCompletionSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
