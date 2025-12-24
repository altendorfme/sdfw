using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

/// <summary>
/// Service for managing network adapters and their DNS settings.
/// </summary>
public interface INetworkAdapterService
{
    /// <summary>
    /// Gets all network adapters.
    /// </summary>
    /// <param name="connectedOnly">If true, only return adapters that are currently connected.</param>
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(bool connectedOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific adapter by ID.
    /// </summary>
    Task<NetworkAdapterInfo?> GetAdapterAsync(string adapterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets DNS servers for an adapter (IPv4).
    /// </summary>
    Task SetIpv4DnsAsync(int interfaceIndex, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets DNS servers for an adapter (IPv6).
    /// </summary>
    Task SetIpv6DnsAsync(int interfaceIndex, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets DNS to DHCP (automatic) for an adapter.
    /// </summary>
    Task ResetToDhcpAsync(int interfaceIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies localhost DNS (127.0.0.1 and ::1) to the specified adapters.
    /// </summary>
    /// <param name="adapterIds">List of adapter IDs to apply to.</param>
    /// <param name="backupFirst">If true, backup existing DNS before applying.</param>
    Task ApplyLocalhostDnsAsync(IReadOnlyList<string> adapterIds, bool backupFirst = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores DNS settings from backup for the specified adapters.
    /// </summary>
    Task RestoreFromBackupAsync(IReadOnlyList<string> adapterIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores all adapters that have backups.
    /// </summary>
    Task RestoreAllFromBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the Windows DNS resolver cache.
    /// </summary>
    Task FlushDnsCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when network adapters change.
    /// </summary>
    event EventHandler? AdaptersChanged;
}
