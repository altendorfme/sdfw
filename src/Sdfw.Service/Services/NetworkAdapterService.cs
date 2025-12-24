using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

[SupportedOSPlatform("windows")]
public sealed class NetworkAdapterService : INetworkAdapterService, IDisposable
{
    private readonly ILogger<NetworkAdapterService> _logger;
    private readonly ISettingsService _settingsService;

    public NetworkAdapterService(ILogger<NetworkAdapterService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public event EventHandler? AdaptersChanged;

    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(bool connectedOnly = true, CancellationToken cancellationToken = default)
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in networkInterfaces)
            {
                try
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                        ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var isConnected = ni.OperationalStatus == OperationalStatus.Up;

                    if (connectedOnly && !isConnected)
                    {
                        continue;
                    }

                    var supportsIpv4 = ni.Supports(NetworkInterfaceComponent.IPv4);
                    var supportsIpv6 = ni.Supports(NetworkInterfaceComponent.IPv6);

                    var ipProps = ni.GetIPProperties();

                    IPv4InterfaceProperties? ipv4Props = null;
                    if (supportsIpv4)
                    {
                        try
                        {
                            ipv4Props = ipProps.GetIPv4Properties();
                        }
                        catch (NetworkInformationException ex)
                        {
                            _logger.LogDebug(ex, "IPv4 properties unavailable for adapter {Name} ({Id})", ni.Name, ni.Id);
                        }
                    }

                    IPv6InterfaceProperties? ipv6Props = null;
                    if (supportsIpv6)
                    {
                        try
                        {
                            ipv6Props = ipProps.GetIPv6Properties();
                        }
                        catch (NetworkInformationException ex)
                        {
                            _logger.LogDebug(ex, "IPv6 properties unavailable for adapter {Name} ({Id})", ni.Name, ni.Id);
                        }
                    }

                    var adapter = new NetworkAdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        AdapterType = ni.NetworkInterfaceType.ToString(),
                        IsConnected = isConnected,
                        SupportsIpv4 = supportsIpv4,
                        SupportsIpv6 = supportsIpv6,
                        InterfaceIndex = ipv4Props?.Index ?? ipv6Props?.Index ?? 0,
                        MacAddress = ni.GetPhysicalAddress().ToString(),
                        Speed = ni.Speed,
                        IsDhcpEnabled = ipv4Props?.IsDhcpEnabled ?? false
                    };

                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            adapter.CurrentIpv4Dns.Add(dns.ToString());
                        }
                        else if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        {
                            adapter.CurrentIpv6Dns.Add(dns.ToString());
                        }
                    }

                    adapters.Add(adapter);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading adapter {Name} ({Id})", ni.Name, ni.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting network adapters");
        }

        return adapters;
    }

    public async Task<NetworkAdapterInfo?> GetAdapterAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        var adapters = await GetAdaptersAsync(connectedOnly: false, cancellationToken);
        return adapters.FirstOrDefault(a => a.Id == adapterId);
    }

    public async Task SetIpv4DnsAsync(int interfaceIndex, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default)
    {
        if (dnsServers.Count == 0)
        {
            await ResetToDhcpAsync(interfaceIndex, cancellationToken);
            return;
        }

        var dnsString = string.Join(",", dnsServers);
        var script = $@"
Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses {dnsString}
";

        await RunPowerShellAsync(script, cancellationToken);
        _logger.LogInformation("Set IPv4 DNS for interface {Index} to {Dns}", interfaceIndex, dnsString);
    }

    public async Task SetIpv6DnsAsync(int interfaceIndex, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default)
    {
        if (dnsServers.Count == 0)
        {
            return;
        }

        var dnsString = string.Join(",", dnsServers);
        var script = $@"
Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses {dnsString}
";

        await RunPowerShellAsync(script, cancellationToken);
        _logger.LogInformation("Set IPv6 DNS for interface {Index} to {Dns}", interfaceIndex, dnsString);
    }

    public async Task ResetToDhcpAsync(int interfaceIndex, CancellationToken cancellationToken = default)
    {
        var script = $@"
Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses
";

        await RunPowerShellAsync(script, cancellationToken);
        _logger.LogInformation("Reset DNS to DHCP for interface {Index}", interfaceIndex);
    }

    public async Task ApplyLocalhostDnsAsync(IReadOnlyList<string> adapterIds, bool backupFirst = true, CancellationToken cancellationToken = default)
    {
        var adapters = await GetAdaptersAsync(connectedOnly: false, cancellationToken);

        foreach (var adapterId in adapterIds)
        {
            var adapter = adapters.FirstOrDefault(a => a.Id == adapterId);
            if (adapter is null)
            {
                _logger.LogWarning("Adapter {Id} not found", adapterId);
                continue;
            }

            if (backupFirst)
            {
                var existingBackup = _settingsService.GetAdapterBackup(adapterId);
                if (existingBackup is null)
                {
                    var backup = new AdapterDnsBackup
                    {
                        AdapterId = adapterId,
                        InterfaceIndex = adapter.InterfaceIndex,
                        AdapterName = adapter.Name,
                        OriginalIpv4Dns = [.. adapter.CurrentIpv4Dns],
                        OriginalIpv6Dns = [.. adapter.CurrentIpv6Dns],
                        WasDhcpEnabled = adapter.IsDhcpEnabled,
                        BackupTimestamp = DateTimeOffset.UtcNow
                    };

                    await _settingsService.SaveAdapterBackupAsync(backup, cancellationToken);
                    _logger.LogInformation("Backed up DNS for adapter {Name}", adapter.Name);
                }
            }

            if (adapter.SupportsIpv4)
            {
                await SetIpv4DnsAsync(adapter.InterfaceIndex, ["127.0.0.1"], cancellationToken);
            }

            if (adapter.SupportsIpv6)
            {
                await SetIpv6DnsAsync(adapter.InterfaceIndex, ["::1"], cancellationToken);
            }

            _logger.LogInformation("Applied localhost DNS to adapter {Name}", adapter.Name);
        }
    }

    public async Task RestoreFromBackupAsync(IReadOnlyList<string> adapterIds, CancellationToken cancellationToken = default)
    {
        foreach (var adapterId in adapterIds)
        {
            var backup = _settingsService.GetAdapterBackup(adapterId);
            if (backup is null)
            {
                _logger.LogWarning("No backup found for adapter {Id}", adapterId);
                continue;
            }

            if (backup.WasDhcpEnabled)
            {
                await ResetToDhcpAsync(backup.InterfaceIndex, cancellationToken);
            }
            else
            {
                if (backup.OriginalIpv4Dns.Count > 0)
                {
                    await SetIpv4DnsAsync(backup.InterfaceIndex, backup.OriginalIpv4Dns, cancellationToken);
                }

                if (backup.OriginalIpv6Dns.Count > 0)
                {
                    await SetIpv6DnsAsync(backup.InterfaceIndex, backup.OriginalIpv6Dns, cancellationToken);
                }
            }

            await _settingsService.RemoveAdapterBackupAsync(adapterId, cancellationToken);
            _logger.LogInformation("Restored DNS for adapter {Name}", backup.AdapterName);
        }
    }

    public async Task RestoreAllFromBackupAsync(CancellationToken cancellationToken = default)
    {
        var backups = _settingsService.Settings.AdapterBackups.ToList();
        var adapterIds = backups.Select(b => b.AdapterId).ToList();
        await RestoreFromBackupAsync(adapterIds, cancellationToken);
    }

    public async Task FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken);
                _logger.LogInformation("DNS cache flushed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing DNS cache");
            throw;
        }
    }

    private async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start PowerShell process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("PowerShell error: {Error}", error);
            throw new InvalidOperationException($"PowerShell command failed: {error}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.LogDebug("PowerShell output: {Output}", output);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        _logger.LogDebug("Network address changed");
        AdaptersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _logger.LogDebug("Network availability changed: {Available}", e.IsAvailable);
        AdaptersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}
