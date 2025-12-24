using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sdfw.Core;
using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Sdfw");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings _settings = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    public AppSettings Settings => _settings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                _logger.LogInformation("Config file not found, creating default settings");
                _settings = CreateDefaultSettings();
                await SaveInternalAsync(cancellationToken);
                return;
            }

            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings is null)
            {
                _logger.LogWarning("Failed to deserialize config, using defaults");
                _settings = CreateDefaultSettings();
                return;
            }

            _settings = settings;
            _logger.LogInformation("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            _settings = CreateDefaultSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SaveInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _settings = settings;
            await SaveInternalAsync(cancellationToken);
            SettingsChanged?.Invoke(this, _settings);
        }
        finally
        {
            _lock.Release();
        }
    }

    public DnsProvider? GetProvider(Guid providerId)
    {
        return _settings.Providers.FirstOrDefault(p => p.Id == providerId);
    }

    public async Task UpsertProviderAsync(DnsProvider provider, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = _settings.Providers.FirstOrDefault(p => p.Id == provider.Id);
            if (existing is not null)
            {
                _settings.Providers.Remove(existing);
            }

            _settings.Providers.Add(provider);
            await SaveInternalAsync(cancellationToken);
            SettingsChanged?.Invoke(this, _settings);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveProviderAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var provider = _settings.Providers.FirstOrDefault(p => p.Id == providerId);
            if (provider is not null)
            {
                _settings.Providers.Remove(provider);
                await SaveInternalAsync(cancellationToken);
                SettingsChanged?.Invoke(this, _settings);
                _logger.LogInformation("Removed provider: {Name} (IsBuiltIn: {IsBuiltIn})", provider.Name, provider.IsBuiltIn);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public AdapterDnsBackup? GetAdapterBackup(string adapterId)
    {
        return _settings.AdapterBackups.FirstOrDefault(b => b.AdapterId == adapterId);
    }

    public async Task SaveAdapterBackupAsync(AdapterDnsBackup backup, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = _settings.AdapterBackups.FirstOrDefault(b => b.AdapterId == backup.AdapterId);
            if (existing is not null)
            {
                _settings.AdapterBackups.Remove(existing);
            }

            _settings.AdapterBackups.Add(backup);
            await SaveInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAdapterBackupAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var backup = _settings.AdapterBackups.FirstOrDefault(b => b.AdapterId == adapterId);
            if (backup is not null)
            {
                _settings.AdapterBackups.Remove(backup);
                await SaveInternalAsync(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            await File.WriteAllTextAsync(ConfigFilePath, json, cancellationToken);
            _logger.LogDebug("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            throw;
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            Version = 1,
            Providers = DefaultProviders.CreateBuiltInProviders(),
            Enabled = false,
            ApplyOnBoot = true,
            UiSettings = new UiSettings
            {
                Language = "pt-BR",
                MinimizeToTray = true,
                CloseToTray = true
            }
        };
    }
}
