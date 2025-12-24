using System.Text.Json.Serialization;

namespace Sdfw.Core.Models;

public sealed class DnsProfile
{
    [JsonPropertyName("providerId")]
    public Guid ProviderId { get; set; }

    [JsonPropertyName("adapterIds")]
    public List<string> AdapterIds { get; set; } = [];
}

public sealed class AdapterDnsBackup
{
    [JsonPropertyName("adapterId")]
    public string AdapterId { get; set; } = string.Empty;

    [JsonPropertyName("interfaceIndex")]
    public int InterfaceIndex { get; set; }

    [JsonPropertyName("adapterName")]
    public string AdapterName { get; set; } = string.Empty;

    [JsonPropertyName("originalIpv4Dns")]
    public List<string> OriginalIpv4Dns { get; set; } = [];

    [JsonPropertyName("originalIpv6Dns")]
    public List<string> OriginalIpv6Dns { get; set; } = [];

    [JsonPropertyName("wasDhcpEnabled")]
    public bool WasDhcpEnabled { get; set; }

    [JsonPropertyName("backupTimestamp")]
    public DateTimeOffset BackupTimestamp { get; set; } = DateTimeOffset.UtcNow;
}
