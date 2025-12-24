using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sdfw.Service.Services;

/// <summary>
/// Hosted service wrapper for health monitoring.
/// </summary>
public sealed class HealthMonitorHostedService : IHostedService
{
    private readonly ILogger<HealthMonitorHostedService> _logger;
    private readonly IHealthMonitorService _healthMonitorService;

    public HealthMonitorHostedService(
        ILogger<HealthMonitorHostedService> logger,
        IHealthMonitorService healthMonitorService)
    {
        _logger = logger;
        _healthMonitorService = healthMonitorService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Health Monitor Hosted Service starting...");
        await _healthMonitorService.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Health Monitor Hosted Service stopping...");
        await _healthMonitorService.StopAsync(cancellationToken);
    }
}
