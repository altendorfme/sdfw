using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;
using Sdfw.Ui.Localization;
using Sdfw.Ui.Services;

namespace Sdfw.Ui.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IIpcClientService _ipcClient;
    private readonly ITrayIconService _trayIconService;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private ConnectionStatus _status = ConnectionStatus.Inactive;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private Brush _statusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string? _activeProviderName;

    [ObservableProperty]
    private string? _defaultProviderName;

    [ObservableProperty]
    private bool _isTemporaryConnection;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _providerDisplayText = string.Empty;

    [ObservableProperty]
    private string _defaultProviderDisplayText = string.Empty;

    public DashboardViewModel(
        IIpcClientService ipcClient,
        ITrayIconService trayIconService,
        ILogger<DashboardViewModel> logger)
    {
        _ipcClient = ipcClient;
        _trayIconService = trayIconService;
        _logger = logger;

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateStatusDisplay();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateStatusDisplay();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var statusResponse = await _ipcClient.GetStatusAsync();
            if (statusResponse is not null)
            {
                Status = statusResponse.Status;
                ActiveProviderName = statusResponse.ActiveProviderName;
                IsTemporaryConnection = statusResponse.IsTemporaryConnection;

                UpdateStatusDisplay();
            }

            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is not null)
            {
                IsEnabled = configResponse.Settings.Enabled;

                if (configResponse.Settings.DefaultProfile is not null)
                {
                    var provider = configResponse.Settings.Providers
                        .FirstOrDefault(p => p.Id == configResponse.Settings.DefaultProfile.ProviderId);
                    DefaultProviderName = provider?.Name;
                }

                UpdateStatusDisplay();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EnableAsync()
    {
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings?.DefaultProfile is null)
            {
                return;
            }

            var response = await _ipcClient.ApplyProfileAsync(
                configResponse.Settings.DefaultProfile,
                enable: true);

            if (response?.Success == true)
            {
                await LoadAsync();
                _trayIconService.ShowNotification("SDfW", Loc.Get("Notification_DnsEnabled"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling DNS protection");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        IsLoading = true;

        try
        {
            await _ipcClient.ShutdownDnsAsync();
            await LoadAsync();
            _trayIconService.ShowNotification("SDfW", Loc.Get("Notification_DnsDisabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling DNS protection");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RevertToDefaultAsync()
    {
        IsLoading = true;

        try
        {
            var response = await _ipcClient.RevertToDefaultAsync();

            if (response?.Success == true)
            {
                await LoadAsync();
                _trayIconService.ShowNotification("SDfW", Loc.Get("Notification_RevertedToDefault"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting to default provider");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task FlushDnsCacheAsync()
    {
        IsLoading = true;

        try
        {
            var response = await _ipcClient.FlushDnsCacheAsync();

            if (response?.Success == true)
            {
                _trayIconService.ShowNotification("SDfW", Loc.Get("Notification_DnsCacheFlushed"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing DNS cache");
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

    private void UpdateStatusDisplay()
    {
        (StatusText, StatusColor) = Status switch
        {
            ConnectionStatus.Connected => (
                IsTemporaryConnection ? Loc.Get("Status_ConnectedTemporary") : Loc.Get("Status_Connected"),
                Brushes.LimeGreen),
            ConnectionStatus.Connecting => (Loc.Get("Status_Connecting"), Brushes.Yellow),
            ConnectionStatus.Testing => (Loc.Get("Status_Testing"), Brushes.DodgerBlue),
            ConnectionStatus.Error => (Loc.Get("Status_Error"), Brushes.Red),
            _ => (Loc.Get("Status_Inactive"), Brushes.Gray)
        };

        ProviderDisplayText = !string.IsNullOrEmpty(ActiveProviderName)
            ? Loc.GetFormat("Dashboard_Provider", ActiveProviderName)
            : Loc.Get("Dashboard_NoActiveProvider");

        if (!string.IsNullOrEmpty(DefaultProviderName))
        {
            DefaultProviderDisplayText = Loc.GetFormat("Dashboard_Default", DefaultProviderName);
        }

        _trayIconService.UpdateStatus(Status);
    }
}
