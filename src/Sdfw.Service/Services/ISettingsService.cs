using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default);

    DnsProvider? GetProvider(Guid providerId);

    Task UpsertProviderAsync(DnsProvider provider, CancellationToken cancellationToken = default);

    Task RemoveProviderAsync(Guid providerId, CancellationToken cancellationToken = default);

    AdapterDnsBackup? GetAdapterBackup(string adapterId);

    Task SaveAdapterBackupAsync(AdapterDnsBackup backup, CancellationToken cancellationToken = default);

    Task RemoveAdapterBackupAsync(string adapterId, CancellationToken cancellationToken = default);

    event EventHandler<AppSettings>? SettingsChanged;
}
