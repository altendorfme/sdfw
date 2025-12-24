using Hardcodet.Wpf.TaskbarNotification;
using Sdfw.Core.Models;

namespace Sdfw.Ui.Services;

/// <summary>
/// Service for managing the system tray icon.
/// </summary>
public interface ITrayIconService
{
    /// <summary>
    /// Initializes and returns the tray icon.
    /// </summary>
    TaskbarIcon Initialize();

    /// <summary>
    /// Updates the tray icon based on connection status.
    /// </summary>
    void UpdateStatus(ConnectionStatus status);

    /// <summary>
    /// Shows a balloon notification.
    /// </summary>
    void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info);
}
