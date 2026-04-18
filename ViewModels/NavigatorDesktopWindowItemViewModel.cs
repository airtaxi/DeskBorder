using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Interop;
using DeskBorder.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace DeskBorder.ViewModels;

public sealed partial class NavigatorDesktopWindowItemViewModel : ObservableObject
{
    private const double IconSizeScaleFactor = 1.25d;
    private const double MaximumIconContainerSize = 240d;
    private const double MinimumIconContainerSize = 90d;
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> s_executableIconImageSourceCache = new(StringComparer.OrdinalIgnoreCase);

    public NavigatorDesktopWindowItemViewModel(NavigatorDesktopWindowItemModel navigatorDesktopWindowItemModel)
    {
        PreviewBounds = navigatorDesktopWindowItemModel.PreviewBounds;
        _ = LoadApplicationIconImageSourceAsync(navigatorDesktopWindowItemModel.WindowHandle, navigatorDesktopWindowItemModel.ExecutablePath);
    }

    public Visibility ApplicationIconVisibility => ApplicationIconImageSource is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility => ApplicationIconImageSource is null ? Visibility.Visible : Visibility.Collapsed;

    public double IconContainerSize => Math.Clamp(Math.Min(PreviewWidth, PreviewHeight) * IconSizeScaleFactor, MinimumIconContainerSize, MaximumIconContainerSize);

    public double PreviewHeight => PreviewBounds.Height;

    public double PreviewLeft => PreviewBounds.Left;

    public double PreviewTop => PreviewBounds.Top;

    public double PreviewWidth => PreviewBounds.Width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplicationIconVisibility))]
    [NotifyPropertyChangedFor(nameof(FallbackIconVisibility))]
    public partial ImageSource? ApplicationIconImageSource { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconContainerSize))]
    [NotifyPropertyChangedFor(nameof(PreviewHeight))]
    [NotifyPropertyChangedFor(nameof(PreviewLeft))]
    [NotifyPropertyChangedFor(nameof(PreviewTop))]
    [NotifyPropertyChangedFor(nameof(PreviewWidth))]
    public partial ScreenRectangle PreviewBounds { get; private set; }

    private static async Task<ImageSource?> CreateApplicationIconImageSourceFromIconAsync(Icon applicationIcon)
    {
        try
        {
            using var applicationBitmap = applicationIcon.ToBitmap();
            using var memoryStream = new MemoryStream();
            applicationBitmap.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            using var inMemoryRandomAccessStream = new InMemoryRandomAccessStream();
            await inMemoryRandomAccessStream.WriteAsync(memoryStream.ToArray().AsBuffer());
            inMemoryRandomAccessStream.Seek(0);

            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(inMemoryRandomAccessStream);
            return bitmapImage;
        }
        catch (ArgumentException) { return null; }
        catch (Win32Exception) { return null; }
        catch (ExternalException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static async Task<ImageSource?> CreateExecutableIconImageSourceAsync(string executablePath)
    {
        try
        {
            using var applicationIcon = Icon.ExtractAssociatedIcon(executablePath);
            return applicationIcon is null ? null : await CreateApplicationIconImageSourceFromIconAsync(applicationIcon);
        }
        catch (ArgumentException) { return null; }
        catch (FileNotFoundException) { return null; }
        catch (OutOfMemoryException) { return null; }
        catch (Win32Exception) { return null; }
        catch (ExternalException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task<ImageSource?> TryCreateWindowIconImageSourceAsync(nint windowHandle)
    {
        var windowIconHandle = TryGetWindowIconHandle(windowHandle);
        if (windowIconHandle == 0)
            return null;

        var copiedIconHandle = Win32.CopyIcon(windowIconHandle);
        if (copiedIconHandle == 0)
            return null;

        try
        {
            using var applicationIcon = Icon.FromHandle(copiedIconHandle);
            return await CreateApplicationIconImageSourceFromIconAsync(applicationIcon);
        }
        catch (ArgumentException) { return null; }
        catch (OutOfMemoryException) { return null; }
        catch (Win32Exception) { return null; }
        catch (ExternalException) { return null; }
        finally { _ = Win32.DestroyIcon(copiedIconHandle); }
    }

    private static nint TryGetWindowIconHandle(nint windowHandle)
    {
        if (windowHandle == 0)
            return 0;

        var iconHandle = Win32.SendMessage(windowHandle, Win32.WindowGetIconMessage, Win32.WindowIconSmallSecondary, 0);
        if (iconHandle != 0)
            return iconHandle;

        iconHandle = Win32.SendMessage(windowHandle, Win32.WindowGetIconMessage, Win32.WindowIconSmall, 0);
        if (iconHandle != 0)
            return iconHandle;

        iconHandle = Win32.SendMessage(windowHandle, Win32.WindowGetIconMessage, Win32.WindowIconBig, 0);
        if (iconHandle != 0)
            return iconHandle;

        iconHandle = Win32.GetClassLongPointer(windowHandle, Win32.ClassLongPointerSmallIcon);
        if (iconHandle != 0)
            return iconHandle;

        return Win32.GetClassLongPointer(windowHandle, Win32.ClassLongPointerIcon);
    }

    private async Task LoadApplicationIconImageSourceAsync(nint windowHandle, string? executablePath)
    {
        ApplicationIconImageSource = await TryCreateWindowIconImageSourceAsync(windowHandle);
        if (ApplicationIconImageSource is not null || string.IsNullOrWhiteSpace(executablePath))
            return;

        var applicationIconImageSourceTask = s_executableIconImageSourceCache.GetOrAdd(executablePath, CreateExecutableIconImageSourceAsync);
        ApplicationIconImageSource = await applicationIconImageSourceTask;
    }
}
