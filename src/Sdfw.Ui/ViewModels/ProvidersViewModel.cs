using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sdfw.Core;
using Sdfw.Core.Ipc;
using Sdfw.Core.Models;
using Sdfw.Ui.Services;
using Sdfw.Ui.Localization;

namespace Sdfw.Ui.ViewModels;

public partial class ProvidersViewModel : ObservableObject
{
    private readonly IIpcClientService _ipcClient;
    private readonly ITrayIconService _trayIconService;
    private readonly ILogger<ProvidersViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<DnsProvider> _providers = [];

    [ObservableProperty]
    private DnsProvider? _selectedProvider;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private Guid? _defaultProviderId;

    [ObservableProperty]
    private Guid? _activeProviderId;

    [ObservableProperty]
    private bool _isTemporaryConnection;

    [ObservableProperty]
    private bool _isServiceConnected;

    public ProvidersViewModel(
        IIpcClientService ipcClient,
        ITrayIconService trayIconService,
        ILogger<ProvidersViewModel> logger)
    {
        _ipcClient = ipcClient;
        _trayIconService = trayIconService;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is not null)
            {
                Providers = new ObservableCollection<DnsProvider>(configResponse.Settings.Providers);
                DefaultProviderId = configResponse.Settings.DefaultProfile?.ProviderId;
                IsServiceConnected = true;

                await EnsureValidDefaultProviderAsync(configResponse.Settings);
            }
            else
            {
                await LoadDefaultProvidersAsync();
                IsServiceConnected = false;
            }

            var statusResponse = await _ipcClient.GetStatusAsync();
            if (statusResponse is not null)
            {
                ActiveProviderId = statusResponse.ActiveProviderId;
                IsTemporaryConnection = statusResponse.IsTemporaryConnection;
            }

            UpdateProviderStates();
            SelectedProvider = Providers.FirstOrDefault(p => p.Id == DefaultProviderId) ?? Providers.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading providers from service, falling back to embedded defaults");
            await LoadDefaultProvidersAsync();
            IsServiceConnected = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task EnsureValidDefaultProviderAsync(AppSettings settings)
    {
        var defaultId = settings.DefaultProfile?.ProviderId;
        var hasValidDefault = defaultId != Guid.Empty &&
                              defaultId != default &&
                              settings.Providers.Any(p => p.Id == defaultId);

        if (hasValidDefault)
        {
            return;
        }

        if (settings.Providers.Count == 0)
        {
            return;
        }

        var firstProvider = settings.Providers[0];

        settings.DefaultProfile ??= new DnsProfile();
        settings.DefaultProfile.ProviderId = firstProvider.Id;

        var save = await _ipcClient.SaveConfigAsync(settings);
        if (save?.Success == true)
        {
            DefaultProviderId = firstProvider.Id;
        }
    }

    private void UpdateProviderStates()
    {
        foreach (var provider in Providers)
        {
            provider.IsDefault = provider.Id == DefaultProviderId;
            provider.IsActive = provider.Id == ActiveProviderId;
            provider.IsTemporaryActive = provider.IsActive && IsTemporaryConnection;
            provider.IsTemporaryToggleEnabled = !(provider.IsActive && !IsTemporaryConnection);
        }

        ReorderProvidersWithDefaultFirst();
    }

    private void ReorderProvidersWithDefaultFirst()
    {
        var defaultProvider = Providers.FirstOrDefault(p => p.IsDefault);
        if (defaultProvider is null) return;

        var currentIndex = Providers.IndexOf(defaultProvider);
        if (currentIndex > 0)
        {
            Providers.Move(currentIndex, 0);
        }
    }

    private async Task LoadDefaultProvidersAsync()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Sdfw.Ui.Data.default-providers.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                var names = assembly.GetManifestResourceNames();
                var matchingName = names.FirstOrDefault(n => n.EndsWith("default-providers.json"));
                if (matchingName is not null)
                {
                    using var fallbackStream = assembly.GetManifestResourceStream(matchingName);
                    if (fallbackStream is not null)
                    {
                        await LoadProvidersFromStreamAsync(fallbackStream);
                        return;
                    }
                }
                return;
            }

            await LoadProvidersFromStreamAsync(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load default providers from embedded resource");
        }
    }

    private async Task LoadProvidersFromStreamAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        
        var data = JsonSerializer.Deserialize<DefaultProvidersFile>(json, options);
        if (data?.Providers is not null)
        {
            Providers = new ObservableCollection<DnsProvider>(data.Providers);
        }
    }

    private class DefaultProvidersFile
    {
        public int Version { get; set; }
        public List<DnsProvider> Providers { get; set; } = [];
    }

    [RelayCommand]
    private async Task ConnectTemporaryAsync(DnsProvider? provider)
    {
        if (provider is null) return;

        if (provider.IsTemporaryActive)
        {
            await DisconnectAsync();
            return;
        }

        if (provider.IsActive && !IsTemporaryConnection)
        {
            _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_DefaultAlreadyActive"));
            return;
        }

        IsLoading = true;

        try
        {
            var response = await _ipcClient.ConnectTemporaryAsync(provider.Id);
            if (response?.Success == true)
            {
                _trayIconService.ShowNotification("SDfW", Loc.GetFormat("Providers_ConnectedTemporary", provider.Name));
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting temporarily to provider {ProviderName}", provider.Name);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings?.DefaultProfile?.ProviderId is not null)
            {
                if (configResponse.Settings.Enabled)
                {
                    var response = await _ipcClient.RevertToDefaultAsync();
                    if (response?.Success == true)
                    {
                        _trayIconService.ShowNotification("SDfW", Loc.Get("Notification_RevertedToDefault"));
                        await LoadAsync();
                    }
                }
                else
                {
                    await _ipcClient.DisableAsync(restoreOriginalDns: true);
                    _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_DisconnectedTemporary"));
                    await LoadAsync();
                }
            }
            else
            {
                await _ipcClient.DisableAsync(restoreOriginalDns: true);
                _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_DisconnectedTemporary"));
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting temporary connection");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetAsDefaultAsync(DnsProvider? provider)
    {
        if (provider is null) return;

        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                return;
            }

            var profile = configResponse.Settings.DefaultProfile ?? new DnsProfile();
            profile.ProviderId = provider.Id;

            var response = await _ipcClient.ApplyProfileAsync(profile, enable: configResponse.Settings.Enabled);
            if (response?.Success == true)
            {
                _trayIconService.ShowNotification("SDfW", Loc.GetFormat("Providers_DefaultSet", provider.Name));
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting {ProviderName} as default provider", provider.Name);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddProvider()
    {
        RaiseShowProviderDialog(null, isNew: true);
    }

    [RelayCommand]
    private void EditProvider(DnsProvider? provider)
    {
        if (provider is null) return;
        RaiseShowProviderDialog(provider, isNew: false);
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(DnsProvider? provider)
    {
        if (provider is null)
        {
            return;
        }

        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                return;
            }

            if (configResponse.Settings.DefaultProfile?.ProviderId == provider.Id)
            {
                _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_CannotDeleteDefault"));
                return;
            }

            configResponse.Settings.Providers.RemoveAll(p => p.Id == provider.Id);
            var response = await _ipcClient.SaveConfigAsync(configResponse.Settings);

            if (response?.Success == true)
            {
                _trayIconService.ShowNotification("SDfW", Loc.GetFormat("Providers_Deleted", provider.Name));
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting provider {ProviderName}", provider.Name);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ResetProvidersAsync()
    {
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                return;
            }

            var currentProviders = configResponse.Settings.Providers;
            var originalBuiltInProviders = DefaultProviders.CreateBuiltInProviders();
            
            var customProviders = currentProviders.Where(p => !p.IsBuiltIn).ToList();
            
            var defaultProviderId = configResponse.Settings.DefaultProfile?.ProviderId;
            var defaultProvider = currentProviders.FirstOrDefault(p => p.Id == defaultProviderId);
            
            if (defaultProvider is not null && !defaultProvider.IsBuiltIn)
            {
                _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_ResetBlockedCustomDefault"));
                return;
            }
            
            if (defaultProviderId.HasValue && !originalBuiltInProviders.Any(p => p.Id == defaultProviderId.Value) && !customProviders.Any(p => p.Id == defaultProviderId.Value))
            {
                configResponse.Settings.DefaultProfile = new DnsProfile
                {
                    ProviderId = originalBuiltInProviders.First().Id
                };
            }

            configResponse.Settings.Providers = [.. originalBuiltInProviders, .. customProviders];
            
            var response = await _ipcClient.SaveConfigAsync(configResponse.Settings);

            if (response?.Success == true)
            {
                _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_ResetSuccess"));
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting providers list");
            _trayIconService.ShowNotification("SDfW", Loc.Get("Providers_ResetFailed"));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event EventHandler<ProviderDialogEventArgs>? ShowProviderDialog;

    public void RaiseShowProviderDialog(DnsProvider? provider, bool isNew)
    {
        ShowProviderDialog?.Invoke(this, new ProviderDialogEventArgs(provider, isNew));
    }

    public async Task<bool> SaveProviderAsync(DnsProvider provider, bool isNew)
    {
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                return false;
            }

            if (isNew)
            {
                provider.Id = Guid.NewGuid();
                configResponse.Settings.Providers.Add(provider);
            }
            else
            {
                var index = configResponse.Settings.Providers.FindIndex(p => p.Id == provider.Id);
                if (index >= 0)
                {
                    configResponse.Settings.Providers[index] = provider;
                }
                else
                {
                    configResponse.Settings.Providers.Add(provider);
                }
            }

            var response = await _ipcClient.SaveConfigAsync(configResponse.Settings);

            if (response?.Success == true)
            {
                await LoadAsync();
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class ProviderDialogEventArgs : EventArgs
{
    public DnsProvider? Provider { get; }
    public bool IsNew { get; }

    public ProviderDialogEventArgs(DnsProvider? provider, bool isNew)
    {
        Provider = provider;
        IsNew = isNew;
    }
}
