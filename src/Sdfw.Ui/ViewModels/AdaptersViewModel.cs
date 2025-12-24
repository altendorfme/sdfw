using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;
using Sdfw.Ui.Services;

namespace Sdfw.Ui.ViewModels;

public partial class AdaptersViewModel : ObservableObject
{
    private readonly IIpcClientService _ipcClient;
    private readonly ILogger<AdaptersViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<NetworkAdapterInfo> _adapters = [];

    [ObservableProperty]
    private ObservableCollection<string> _selectedAdapterIds = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showAllAdapters;

    [ObservableProperty]
    private bool _isServiceConnected;

    public AdaptersViewModel(IIpcClientService ipcClient, ILogger<AdaptersViewModel> logger)
    {
        _ipcClient = ipcClient;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var adaptersResponse = await _ipcClient.GetAdaptersAsync(connectedOnly: !ShowAllAdapters);
            if (adaptersResponse?.Adapters is not null)
            {
                Adapters = new ObservableCollection<NetworkAdapterInfo>(adaptersResponse.Adapters);
                IsServiceConnected = true;
            }
            else
            {
                LoadAdaptersFromSystem();
                IsServiceConnected = false;
            }

            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings?.DefaultProfile is not null)
            {
                SelectedAdapterIds = new ObservableCollection<string>(
                    configResponse.Settings.DefaultProfile.AdapterIds);
                
                foreach (var adapter in Adapters)
                {
                    adapter.IsSelected = SelectedAdapterIds.Contains(adapter.Id);
                }
            }
        }
        catch (Exception)
        {
            LoadAdaptersFromSystem();
            IsServiceConnected = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadAdaptersFromSystem()
    {
        var adapters = new List<NetworkAdapterInfo>();
        
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            
            foreach (var nic in interfaces)
            {
                if (!ShowAllAdapters)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;
                }

                var ipProps = nic.GetIPProperties();
                var ipv4Dns = ipProps.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();
                
                var ipv6Dns = ipProps.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(a => a.ToString())
                    .ToList();

                var adapter = new NetworkAdapterInfo
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    IsConnected = nic.OperationalStatus == OperationalStatus.Up,
                    AdapterType = nic.NetworkInterfaceType.ToString(),
                    CurrentIpv4Dns = ipv4Dns,
                    CurrentIpv6Dns = ipv6Dns,
                    IsSelected = SelectedAdapterIds.Contains(nic.Id)
                };

                adapters.Add(adapter);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating network adapters from system");
        }

        Adapters = new ObservableCollection<NetworkAdapterInfo>(adapters);
    }

    [RelayCommand]
    private void ToggleAdapterSelection(NetworkAdapterInfo? adapter)
    {
        if (adapter is null) return;

        if (SelectedAdapterIds.Contains(adapter.Id))
        {
            SelectedAdapterIds.Remove(adapter.Id);
        }
        else
        {
            SelectedAdapterIds.Add(adapter.Id);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var adapter in Adapters)
        {
            adapter.IsSelected = true;
        }
        SelectedAdapterIds = new ObservableCollection<string>(Adapters.Select(a => a.Id));
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var adapter in Adapters)
        {
            adapter.IsSelected = false;
        }
        SelectedAdapterIds.Clear();
    }

    [RelayCommand]
    private async Task SaveSelectionAsync()
    {
        var selectedIds = Adapters.Where(a => a.IsSelected).Select(a => a.Id).ToList();
        IsLoading = true;

        try
        {
            var configResponse = await _ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                return;
            }

            var profile = configResponse.Settings.DefaultProfile ?? new DnsProfile();
            profile.AdapterIds = selectedIds;

            configResponse.Settings.DefaultProfile = profile;
            var saveResponse = await _ipcClient.SaveConfigAsync(configResponse.Settings);

            if (saveResponse?.Success == true)
            {
                SelectedAdapterIds = new ObservableCollection<string>(selectedIds);
            }

            if (configResponse.Settings.Enabled)
            {
                var applyResponse = await _ipcClient.ApplyProfileAsync(profile, enable: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving adapter selection");
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

    partial void OnShowAllAdaptersChanged(bool value)
    {
        _ = LoadAsync();
    }
}
