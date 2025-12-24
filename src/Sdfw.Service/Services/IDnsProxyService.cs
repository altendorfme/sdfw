using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

/// <summary>
/// Service for managing the DNS proxy server.
/// </summary>
public interface IDnsProxyService
{
    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    ConnectionStatus Status { get; }

    /// <summary>
    /// Gets the currently active provider (may be temporary).
    /// </summary>
    DnsProvider? ActiveProvider { get; }

    /// <summary>
    /// Gets whether the current connection is temporary (not the default).
    /// </summary>
    bool IsTemporaryConnection { get; }

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Gets the count of DNS queries handled since start.
    /// </summary>
    long QueriesHandled { get; }

    /// <summary>
    /// Gets the timestamp of the last health check.
    /// </summary>
    DateTimeOffset? LastHealthCheck { get; }

    /// <summary>
    /// Starts the DNS proxy with the specified provider.
    /// </summary>
    Task StartAsync(DnsProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the DNS proxy.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to a different provider (temporary connection).
    /// </summary>
    Task SwitchProviderAsync(DnsProvider provider, bool isTemporary = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to a provider without switching.
    /// </summary>
    /// <returns>Latency in milliseconds, or null if failed.</returns>
    Task<(bool Success, double? LatencyMs, string? Error)> TestProviderAsync(DnsProvider provider, string testDomain = "example.com", CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the current connection.
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when status changes.
    /// </summary>
    event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Event raised when an error occurs.
    /// </summary>
    event EventHandler<string>? ErrorOccurred;
}
