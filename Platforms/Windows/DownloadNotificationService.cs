using System.Runtime.InteropServices;
using Autonomuse.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Autonomuse.Platforms.Windows;

public sealed class DownloadNotificationService : IDownloadNotificationService, IDisposable
{
    private readonly ILogger<DownloadNotificationService> _logger;
    private bool _registered;

    public DownloadNotificationService(ILogger<DownloadNotificationService> logger)
    {
        _logger = logger;
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnNotificationInvoked;
        try
        {
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register Windows download notifications.");
        }
    }

    public void ShowCompleted(bool isAudio, int downloaded, int errors)
    {
        if (!_registered || downloaded <= 0) return;
        try
        {
            var media = isAudio ? "Audio" : "Video";
            var message = $"{downloaded} {media.ToLowerInvariant()} file(s) added to your library.";
            if (errors > 0) message += $" {errors} download(s) failed.";
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "openAutonomuse")
                .AddText($"{media} download finished")
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show Windows download notification.");
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                is not Microsoft.UI.Xaml.Window window) return;

            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            // Restore minimized windows without changing the size of other windows.
            if (IsIconic(handle)) ShowWindow(handle, 9);
            window.AppWindow.Show();
            window.Activate();
            SetForegroundWindow(handle);
        });
    }

    public void Dispose()
    {
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);
}
