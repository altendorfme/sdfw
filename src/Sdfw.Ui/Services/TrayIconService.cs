using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;
using Sdfw.Ui.Localization;

namespace Sdfw.Ui.Services;

public sealed class TrayIconService : ITrayIconService
{
    private readonly ILogger<TrayIconService> _logger;
    private TaskbarIcon? _trayIcon;
    private ConnectionStatus _currentStatus = ConnectionStatus.Inactive;
    private Icon? _appIcon;
    private readonly Dictionary<ConnectionStatus, Icon?> _statusIcons = new();

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
    }

    public TaskbarIcon Initialize()
    {
        _appIcon = LoadAppIcon();
        LoadStatusIcons();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = Loc.GetFormat("Tray_Tooltip", Loc.Get("Status_Inactive")),
            Icon = GetStatusIcon(ConnectionStatus.Inactive) ?? _appIcon ?? CreateFallbackIcon(ConnectionStatus.Inactive),
            ContextMenu = CreateContextMenu()
        };

        _trayIcon.TrayMouseDoubleClick += OnTrayDoubleClick;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        return _trayIcon;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.ContextMenu = CreateContextMenu();
            _trayIcon.ToolTipText = Loc.GetFormat("Tray_Tooltip", GetStatusText(_currentStatus));
        }
    }

    private Icon? LoadAppIcon()
    {
        try
        {
            var resourceUri = new Uri("pack://application:,,,/Sdfw.Ui;component/Assets/app.ico", UriKind.Absolute);
            var resourceInfo = Application.GetResourceStream(resourceUri);
            if (resourceInfo?.Stream != null)
            {
                return new Icon(resourceInfo.Stream);
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public void UpdateStatus(ConnectionStatus status)
    {
        if (_trayIcon is null) return;

        _currentStatus = status;
        _trayIcon.Icon = GetStatusIcon(status) ?? CreateFallbackIcon(status);
        _trayIcon.ToolTipText = Loc.GetFormat("Tray_Tooltip", GetStatusText(status));
    }

    public void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        _trayIcon?.ShowBalloonTip(title, message, icon);
    }

    private void LoadStatusIcons()
    {
        var iconMapping = new Dictionary<ConnectionStatus, string>
        {
            { ConnectionStatus.Connected, "green.ico" },
            { ConnectionStatus.Connecting, "gray.ico" },
            { ConnectionStatus.Testing, "gray.ico" },
            { ConnectionStatus.Error, "red.ico" },
            { ConnectionStatus.Inactive, "gray.ico" }
        };

        foreach (var (status, iconName) in iconMapping)
        {
            try
            {
                var resourceUri = new Uri($"pack://application:,,,/Sdfw.Ui;component/Assets/{iconName}", UriKind.Absolute);
                var resourceInfo = Application.GetResourceStream(resourceUri);
                if (resourceInfo?.Stream != null)
                {
                    _statusIcons[status] = new Icon(resourceInfo.Stream);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private Icon? GetStatusIcon(ConnectionStatus status)
    {
        return _statusIcons.TryGetValue(status, out var icon) ? icon : null;
    }

    private static Icon CreateFallbackIcon(ConnectionStatus status)
    {
        var color = status switch
        {
            ConnectionStatus.Connected => Color.Green,
            ConnectionStatus.Error => Color.Red,
            _ => Color.Gray
        };

        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 2, 2, 12, 12);

        using var pen = new Pen(Color.DarkGray, 1);
        graphics.DrawEllipse(pen, 2, 2, 12, 12);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static string GetStatusText(ConnectionStatus status)
    {
        return status switch
        {
            ConnectionStatus.Connected => Loc.Get("Status_Connected"),
            ConnectionStatus.Connecting => Loc.Get("Status_Connecting"),
            ConnectionStatus.Testing => Loc.Get("Status_Testing"),
            ConnectionStatus.Error => Loc.Get("Status_Error"),
            _ => Loc.Get("Status_Inactive")
        };
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = Loc.Get("Tray_Open") };
        openItem.Click += OnOpenClick;
        menu.Items.Add(openItem);

        menu.Items.Add(new Separator());

        var clearCacheItem = new MenuItem { Header = Loc.Get("Tray_FlushDns") };
        clearCacheItem.Click += OnClearCacheClick;
        menu.Items.Add(clearCacheItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = Loc.Get("Tray_Exit") };
        exitItem.Click += OnExitClick;
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var ipcClient = App.Services.GetService(typeof(IIpcClientService)) as IIpcClientService;
            if (ipcClient is null) return;

            var response = await ipcClient.FlushDnsCacheAsync();
            if (response?.Success == true)
            {
                ShowNotification("SDfW", Loc.Get("Notification_DnsCacheFlushed"), BalloonIcon.Info);
            }
            else
            {
                ShowNotification("SDfW", response?.ErrorMessage ?? Loc.Get("Notification_ErrorFlushingCache"), BalloonIcon.Error);
            }
        }
        catch (Exception)
        {
            ShowNotification("SDfW", Loc.Get("Notification_ErrorFlushingCache"), BalloonIcon.Error);
        }
    }

    private async void OnExitClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var ipcClient = App.Services.GetService(typeof(IIpcClientService)) as IIpcClientService;
            if (ipcClient is not null)
            {
                await ipcClient.ShutdownDnsAsync();

                try
                {
                    await ipcClient.DisconnectAsync();
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private static void ShowMainWindow()
    {
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow is null) return;

        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }
}
