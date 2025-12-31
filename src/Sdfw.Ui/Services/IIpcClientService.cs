using Sdfw.Core.Ipc;
using Sdfw.Core.Models;

namespace Sdfw.Ui.Services;

/// <summary>
/// Service for communicating with the SDfW Windows Service via named pipes.
/// </summary>
public interface IIpcClientService
{
    /// <summary>
    /// Gets whether the client is connected to the service.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the service.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the service.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Gets the current service status.
    /// </summary>
    Task<GetStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    Task<GetConfigResponse?> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves configuration.
    /// </summary>
    Task<SaveConfigResponse?> SaveConfigAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets network adapters.
    /// </summary>
    Task<GetAdaptersResponse?> GetAdaptersAsync(bool connectedOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a profile and enables DNS protection.
    /// </summary>
    Task<ApplyProfileResponse?> ApplyProfileAsync(DnsProfile profile, bool enable = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to a provider temporarily without saving as default.
    /// </summary>
    Task<ConnectTemporaryResponse?> ConnectTemporaryAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts to the default provider.
    /// </summary>
    Task<RevertToDefaultResponse?> RevertToDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables DNS protection and optionally restores original DNS.
    /// </summary>
    Task<DisableResponse?> DisableAsync(bool restoreOriginalDns = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests a provider's connectivity.
    /// </summary>
    Task<TestProviderResponse?> TestProviderAsync(Guid providerId, string testDomain = "example.com", CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the DNS cache.
    /// </summary>
    Task<FlushDnsCacheResponse?> FlushDnsCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a unified shutdown: revert to default (if temporary), flush DNS cache, and disable DNS protection.
    /// This standardized flow ensures consistent cleanup across all shutdown scenarios.
    /// </summary>
    Task ShutdownDnsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a notification is received from the service.
    /// </summary>
    event EventHandler<IpcMessage>? NotificationReceived;

    /// <summary>
    /// Event raised when connection status changes.
    /// </summary>
    event EventHandler<bool>? ConnectionChanged;
}
