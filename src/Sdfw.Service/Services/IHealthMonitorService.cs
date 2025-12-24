namespace Sdfw.Service.Services;

/// <summary>
/// Service for monitoring the health of DNS connections.
/// </summary>
public interface IHealthMonitorService
{
    /// <summary>
    /// Gets whether health monitoring is currently active.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Gets the interval between health checks.
    /// </summary>
    TimeSpan CheckInterval { get; set; }

    /// <summary>
    /// Starts health monitoring.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops health monitoring.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces an immediate health check.
    /// </summary>
    Task<bool> CheckNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a health check completes.
    /// </summary>
    event EventHandler<HealthCheckResult>? HealthCheckCompleted;
}

/// <summary>
/// Result of a health check.
/// </summary>
public sealed record HealthCheckResult
{
    /// <summary>
    /// Whether the check passed.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Latency of the check in milliseconds.
    /// </summary>
    public double? LatencyMs { get; init; }

    /// <summary>
    /// Error message if the check failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of the check.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
