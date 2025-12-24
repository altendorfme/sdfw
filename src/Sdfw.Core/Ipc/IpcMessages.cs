using System.Text.Json.Serialization;
using Sdfw.Core.Models;

namespace Sdfw.Core.Ipc;

/// <summary>
/// Base class for all IPC messages.
/// </summary>
[JsonDerivedType(typeof(GetStatusRequest), "GetStatusRequest")]
[JsonDerivedType(typeof(GetStatusResponse), "GetStatusResponse")]
[JsonDerivedType(typeof(GetConfigRequest), "GetConfigRequest")]
[JsonDerivedType(typeof(GetConfigResponse), "GetConfigResponse")]
[JsonDerivedType(typeof(SaveConfigRequest), "SaveConfigRequest")]
[JsonDerivedType(typeof(SaveConfigResponse), "SaveConfigResponse")]
[JsonDerivedType(typeof(GetAdaptersRequest), "GetAdaptersRequest")]
[JsonDerivedType(typeof(GetAdaptersResponse), "GetAdaptersResponse")]
[JsonDerivedType(typeof(ApplyProfileRequest), "ApplyProfileRequest")]
[JsonDerivedType(typeof(ApplyProfileResponse), "ApplyProfileResponse")]
[JsonDerivedType(typeof(ConnectTemporaryRequest), "ConnectTemporaryRequest")]
[JsonDerivedType(typeof(ConnectTemporaryResponse), "ConnectTemporaryResponse")]
[JsonDerivedType(typeof(RevertToDefaultRequest), "RevertToDefaultRequest")]
[JsonDerivedType(typeof(RevertToDefaultResponse), "RevertToDefaultResponse")]
[JsonDerivedType(typeof(DisableRequest), "DisableRequest")]
[JsonDerivedType(typeof(DisableResponse), "DisableResponse")]
[JsonDerivedType(typeof(TestProviderRequest), "TestProviderRequest")]
[JsonDerivedType(typeof(TestProviderResponse), "TestProviderResponse")]
[JsonDerivedType(typeof(FlushDnsCacheRequest), "FlushDnsCacheRequest")]
[JsonDerivedType(typeof(FlushDnsCacheResponse), "FlushDnsCacheResponse")]
[JsonDerivedType(typeof(StatusChangedNotification), "StatusChangedNotification")]
[JsonDerivedType(typeof(ErrorNotification), "ErrorNotification")]
public abstract class IpcMessage
{
    [JsonPropertyName("messageId")]
    public Guid MessageId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

// ============ Status ============

public sealed class GetStatusRequest : IpcMessage;

public sealed class GetStatusResponse : IpcMessage
{
    [JsonPropertyName("status")]
    public ConnectionStatus Status { get; set; }

    [JsonPropertyName("activeProviderId")]
    public Guid? ActiveProviderId { get; set; }

    [JsonPropertyName("activeProviderName")]
    public string? ActiveProviderName { get; set; }

    [JsonPropertyName("isTemporaryConnection")]
    public bool IsTemporaryConnection { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("lastHealthCheck")]
    public DateTimeOffset? LastHealthCheck { get; set; }

    [JsonPropertyName("queriesHandled")]
    public long QueriesHandled { get; set; }
}

// ============ Config ============

public sealed class GetConfigRequest : IpcMessage;

public sealed class GetConfigResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("settings")]
    public AppSettings? Settings { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public sealed class SaveConfigRequest : IpcMessage
{
    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();
}

public sealed class SaveConfigResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Adapters ============

public sealed class GetAdaptersRequest : IpcMessage
{
    /// <summary>
    /// If true, only return adapters that are currently connected.
    /// </summary>
    [JsonPropertyName("connectedOnly")]
    public bool ConnectedOnly { get; set; } = true;
}

public sealed class GetAdaptersResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("adapters")]
    public List<NetworkAdapterInfo> Adapters { get; set; } = [];

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Apply Profile ============

public sealed class ApplyProfileRequest : IpcMessage
{
    /// <summary>
    /// The profile to apply and save as default.
    /// </summary>
    [JsonPropertyName("profile")]
    public DnsProfile Profile { get; set; } = new();

    /// <summary>
    /// If true, also enable DNS protection.
    /// </summary>
    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = true;
}

public sealed class ApplyProfileResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Temporary Connect ============

public sealed class ConnectTemporaryRequest : IpcMessage
{
    /// <summary>
    /// The provider ID to connect to temporarily (without overwriting default profile).
    /// </summary>
    [JsonPropertyName("providerId")]
    public Guid ProviderId { get; set; }
}

public sealed class ConnectTemporaryResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Revert to Default ============

public sealed class RevertToDefaultRequest : IpcMessage;

public sealed class RevertToDefaultResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Disable ============

public sealed class DisableRequest : IpcMessage
{
    /// <summary>
    /// If true, restore adapters to their original DNS settings.
    /// </summary>
    [JsonPropertyName("restoreOriginalDns")]
    public bool RestoreOriginalDns { get; set; } = true;
}

public sealed class DisableResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Test Provider ============

public sealed class TestProviderRequest : IpcMessage
{
    [JsonPropertyName("providerId")]
    public Guid ProviderId { get; set; }

    /// <summary>
    /// Domain to query for testing (defaults to "example.com").
    /// </summary>
    [JsonPropertyName("testDomain")]
    public string TestDomain { get; set; } = "example.com";
}

public sealed class TestProviderResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("latencyMs")]
    public double? LatencyMs { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Flush DNS Cache ============

public sealed class FlushDnsCacheRequest : IpcMessage;

public sealed class FlushDnsCacheResponse : IpcMessage
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============ Notifications (Service -> UI) ============

public sealed class StatusChangedNotification : IpcMessage
{
    [JsonPropertyName("newStatus")]
    public ConnectionStatus NewStatus { get; set; }

    [JsonPropertyName("previousStatus")]
    public ConnectionStatus PreviousStatus { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class ErrorNotification : IpcMessage
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("isCritical")]
    public bool IsCritical { get; set; }
}
