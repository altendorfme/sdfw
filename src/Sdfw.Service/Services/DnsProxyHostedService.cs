using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sdfw.Service.Services;

public sealed class DnsProxyHostedService : IHostedService
{
    private readonly ILogger<DnsProxyHostedService> _logger;
    private readonly IDnsProxyService _dnsProxyService;
    private readonly ISettingsService _settingsService;
    private readonly INetworkAdapterService _networkAdapterService;

    public DnsProxyHostedService(
        ILogger<DnsProxyHostedService> logger,
        IDnsProxyService dnsProxyService,
        ISettingsService settingsService,
        INetworkAdapterService networkAdapterService)
    {
        _logger = logger;
        _dnsProxyService = dnsProxyService;
        _settingsService = settingsService;
        _networkAdapterService = networkAdapterService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DNS Proxy Hosted Service starting...");

        await _settingsService.LoadAsync(cancellationToken);

        var settings = _settingsService.Settings;

        if (settings.Enabled && settings.ApplyOnBoot && settings.DefaultProfile is not null)
        {
            var provider = _settingsService.GetProvider(settings.DefaultProfile.ProviderId);
            if (provider is not null)
            {
                _logger.LogInformation("Auto-starting DNS proxy with provider: {Provider}", provider.Name);

                try
                {
                    await _networkAdapterService.ApplyLocalhostDnsAsync(
                        settings.DefaultProfile.AdapterIds,
                        backupFirst: true,
                        cancellationToken);

                    await _dnsProxyService.StartAsync(provider, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-start DNS proxy");
                }
            }
            else
            {
                _logger.LogWarning("Default provider not found: {Id}", settings.DefaultProfile.ProviderId);
            }
        }
        else
        {
            _logger.LogInformation("DNS proxy not configured for auto-start");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DNS Proxy Hosted Service stopping...");

        try
        {
            await _dnsProxyService.StopAsync(cancellationToken);
            await _networkAdapterService.RestoreAllFromBackupAsync(cancellationToken);
            _logger.LogInformation("DNS settings restored successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown");
        }
    }
}
