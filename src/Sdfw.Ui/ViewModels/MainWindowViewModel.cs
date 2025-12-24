using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;
using Sdfw.Ui.Services;

namespace Sdfw.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IIpcClientService _ipcClient;
    private readonly ITrayIconService _trayIconService;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private ConnectionStatus _status = ConnectionStatus.Inactive;

    [ObservableProperty]
    private bool _isConnectedToService;

    [ObservableProperty]
    private string? _activeProviderName;

    [ObservableProperty]
    private bool _isTemporaryConnection;

    public MainWindowViewModel(
        IIpcClientService ipcClient,
        ITrayIconService trayIconService,
        ILogger<MainWindowViewModel> logger)
    {
        _ipcClient = ipcClient;
        _trayIconService = trayIconService;
        _logger = logger;

        _ipcClient.ConnectionChanged += OnServiceConnectionChanged;
    }

    public async Task InitializeAsync()
    {
        await RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var response = await _ipcClient.GetStatusAsync();
            if (response is not null)
            {
                UpdateStatus(response.Status, response.ActiveProviderName, response.IsTemporaryConnection);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing status");
        }
    }

    private void OnServiceConnectionChanged(object? sender, bool isConnected)
    {
        IsConnectedToService = isConnected;

        if (!isConnected)
        {
            UpdateStatus(ConnectionStatus.Inactive, null, false);
        }
    }

    private void UpdateStatus(ConnectionStatus status, string? providerName, bool isTemporary)
    {
        Status = status;
        ActiveProviderName = providerName;
        IsTemporaryConnection = isTemporary;

        _trayIconService.UpdateStatus(status);
    }
}
