using Microsoft.Extensions.DependencyInjection;
using Sdfw.Service.Services;

namespace Sdfw.Service;

public static class SdfwServiceRegistration
{
    public static IServiceCollection AddSdfwService(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INetworkAdapterService, NetworkAdapterService>();
        services.AddSingleton<IDnsProxyService, DnsProxyService>();
        services.AddSingleton<IIpcServerService, IpcServerService>();
        services.AddSingleton<IHealthMonitorService, HealthMonitorService>();

        services.AddHostedService<DnsProxyHostedService>();
        services.AddHostedService<IpcServerHostedService>();
        services.AddHostedService<HealthMonitorHostedService>();

        return services;
    }
}
