using System.Text.Json.Serialization;

namespace Sdfw.Core.Models;

public sealed class AppSettings
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("providers")]
    public List<DnsProvider> Providers { get; set; } = [];

    [JsonPropertyName("defaultProfile")]
    public DnsProfile? DefaultProfile { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("applyOnBoot")]
    public bool ApplyOnBoot { get; set; } = true;

    [JsonPropertyName("adapterBackups")]
    public List<AdapterDnsBackup> AdapterBackups { get; set; } = [];

    [JsonPropertyName("bootstrapDnsServers")]
    public List<string> BootstrapDnsServers { get; set; } = [];

    [JsonPropertyName("uiSettings")]
    public UiSettings UiSettings { get; set; } = new();
}

public sealed class UiSettings
{
    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "pt-BR";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "System";

    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; } = true;
}
