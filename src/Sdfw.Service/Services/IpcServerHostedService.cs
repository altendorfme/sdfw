using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sdfw.Service.Services;

/// <summary>
/// Hosted service wrapper for IPC server.
/// </summary>
public sealed class IpcServerHostedService : IHostedService
{
    private readonly ILogger<IpcServerHostedService> _logger;
    private readonly IIpcServerService _ipcServerService;

    public IpcServerHostedService(
        ILogger<IpcServerHostedService> logger,
        IIpcServerService ipcServerService)
    {
        _logger = logger;
        _ipcServerService = ipcServerService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IPC Server Hosted Service starting...");
        await _ipcServerService.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IPC Server Hosted Service stopping...");
        await _ipcServerService.StopAsync(cancellationToken);
    }
}
