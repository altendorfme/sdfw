using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Sdfw.Core.Models;

public sealed class DnsProvider : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public DnsProviderType Type { get; set; } = DnsProviderType.Standard;

    [JsonPropertyName("primaryIpv4")]
    public string? PrimaryIpv4 { get; set; }

    [JsonPropertyName("secondaryIpv4")]
    public string? SecondaryIpv4 { get; set; }

    [JsonPropertyName("primaryIpv6")]
    public string? PrimaryIpv6 { get; set; }

    [JsonPropertyName("secondaryIpv6")]
    public string? SecondaryIpv6 { get; set; }

    [JsonPropertyName("dohUrl")]
    public string? DohUrl { get; set; }

    [JsonPropertyName("bootstrapIps")]
    public List<string> BootstrapIps { get; set; } = [];

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    private bool _isDefault;

    [JsonIgnore]
    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault == value) return;
            _isDefault = value;
            OnPropertyChanged();
        }
    }

    private bool _isActive;

    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    private bool _isTemporaryActive;

    [JsonIgnore]
    public bool IsTemporaryActive
    {
        get => _isTemporaryActive;
        set
        {
            if (_isTemporaryActive == value) return;
            _isTemporaryActive = value;
            OnPropertyChanged();
        }
    }

    private bool _isTemporaryToggleEnabled = true;

    [JsonIgnore]
    public bool IsTemporaryToggleEnabled
    {
        get => _isTemporaryToggleEnabled;
        set
        {
            if (_isTemporaryToggleEnabled == value) return;
            _isTemporaryToggleEnabled = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> Ipv4Addresses
    {
        get
        {
            var addresses = new List<string>();
            if (!string.IsNullOrWhiteSpace(PrimaryIpv4))
            {
                addresses.Add(PrimaryIpv4);
            }

            if (!string.IsNullOrWhiteSpace(SecondaryIpv4))
            {
                addresses.Add(SecondaryIpv4);
            }

            return addresses;
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> Ipv6Addresses
    {
        get
        {
            var addresses = new List<string>();
            if (!string.IsNullOrWhiteSpace(PrimaryIpv6))
            {
                addresses.Add(PrimaryIpv6);
            }

            if (!string.IsNullOrWhiteSpace(SecondaryIpv6))
            {
                addresses.Add(SecondaryIpv6);
            }

            return addresses;
        }
    }
}
