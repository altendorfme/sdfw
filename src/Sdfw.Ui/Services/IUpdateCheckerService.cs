namespace Sdfw.Ui.Services;

/// <summary>
/// Information about an available update.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>
    /// The latest version available.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// URL to download the update.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable { get; set; }
}

/// <summary>
/// Service to check for application updates.
/// </summary>
public interface IUpdateCheckerService
{
    /// <summary>
    /// Gets the current application version.
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Checks for available updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update information if check succeeds, null otherwise.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
