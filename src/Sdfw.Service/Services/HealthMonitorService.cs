using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

/// <summary>
/// Implementation of health monitoring service.
/// </summary>
public sealed class HealthMonitorService : IHealthMonitorService
{
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly IDnsProxyService _dnsProxyService;
    private readonly IIpcServerService _ipcServerService;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public HealthMonitorService(
        ILogger<HealthMonitorService> logger,
        IDnsProxyService dnsProxyService,
        IIpcServerService ipcServerService)
    {
        _logger = logger;
        _dnsProxyService = dnsProxyService;
        _ipcServerService = ipcServerService;
    }

    public bool IsMonitoring => _monitorTask is not null && !_monitorTask.IsCompleted;

    public TimeSpan CheckInterval
    {
        get => _checkInterval;
        set => _checkInterval = value;
    }

    public event EventHandler<HealthCheckResult>? HealthCheckCompleted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorAsync(_cts.Token);
        _logger.LogInformation("Health monitor started with interval: {Interval}", _checkInterval);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping health monitor...");
        _cts?.Cancel();

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Monitor task did not complete within timeout");
            }
        }

        _logger.LogInformation("Health monitor stopped");
    }

    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        return await PerformHealthCheckAsync(cancellationToken);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, cancellationToken);

                if (_dnsProxyService.Status == ConnectionStatus.Connected)
                {
                    await PerformHealthCheckAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check");
            }
        }
    }

    private async Task<bool> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new HealthCheckResult();

        try
        {
            var isHealthy = await _dnsProxyService.HealthCheckAsync(cancellationToken);
            sw.Stop();

            result = new HealthCheckResult
            {
                IsHealthy = isHealthy,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (!isHealthy)
            {
                result = result with { ErrorMessage = _dnsProxyService.LastError };

                // Notify UI about the error
                await _ipcServerService.BroadcastAsync(
                    new Core.Ipc.StatusChangedNotification
                    {
                        NewStatus = ConnectionStatus.Error,
                        PreviousStatus = ConnectionStatus.Connected,
                        Message = _dnsProxyService.LastError
                    },
                    cancellationToken);
            }

            _logger.LogDebug("Health check completed: {IsHealthy}, Latency: {Latency}ms",
                result.IsHealthy, result.LatencyMs);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result = new HealthCheckResult
            {
                IsHealthy = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTimeOffset.UtcNow
            };

            _logger.LogError(ex, "Health check failed");
        }

        HealthCheckCompleted?.Invoke(this, result);
        return result.IsHealthy;
    }
}
