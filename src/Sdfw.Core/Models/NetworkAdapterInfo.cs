using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Sdfw.Core.Models;

public sealed class NetworkAdapterInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    [JsonPropertyName("interfaceIndex")]
    public int InterfaceIndex { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("adapterType")]
    public string? AdapterType { get; set; }

    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    [JsonPropertyName("supportsIpv4")]
    public bool SupportsIpv4 { get; set; }

    [JsonPropertyName("supportsIpv6")]
    public bool SupportsIpv6 { get; set; }

    [JsonPropertyName("currentIpv4Dns")]
    public List<string> CurrentIpv4Dns { get; set; } = [];

    [JsonPropertyName("currentIpv6Dns")]
    public List<string> CurrentIpv6Dns { get; set; } = [];

    [JsonPropertyName("isDhcpEnabled")]
    public bool IsDhcpEnabled { get; set; }

    [JsonPropertyName("macAddress")]
    public string? MacAddress { get; set; }

    [JsonPropertyName("speed")]
    public long Speed { get; set; }

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
