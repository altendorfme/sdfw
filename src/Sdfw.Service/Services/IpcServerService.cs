using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Ipc;
using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

public sealed class IpcServerService : IIpcServerService, IDisposable
{
    public const string PipeName = "SdfwServicePipe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<IpcServerService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IDnsProxyService _dnsProxyService;

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private readonly List<NamedPipeServerStream> _connectedClients = [];
    private readonly object _clientsLock = new();

    public IpcServerService(
        ILogger<IpcServerService> logger,
        ISettingsService settingsService,
        INetworkAdapterService networkAdapterService,
        IDnsProxyService dnsProxyService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _networkAdapterService = networkAdapterService;
        _dnsProxyService = dnsProxyService;
    }

    public bool IsRunning => _listenerTask is not null && !_listenerTask.IsCompleted;

    public int ConnectedClients
    {
        get
        {
            lock (_clientsLock)
            {
                return _connectedClients.Count;
            }
        }
    }

    public event EventHandler? ClientConnected;
    public event EventHandler? ClientDisconnected;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenerTask = ListenAsync(_cts.Token);
        _logger.LogInformation("IPC server started on pipe: {PipeName}", PipeName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping IPC server...");
        _cts?.Cancel();

        lock (_clientsLock)
        {
            foreach (var client in _connectedClients)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }

            _connectedClients.Clear();
        }

        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Listener task did not complete within timeout");
            }
        }

        _logger.LogInformation("IPC server stopped");
    }

    public async Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        List<NamedPipeServerStream> clientsCopy;
        lock (_clientsLock)
        {
            clientsCopy = [.. _connectedClients];
        }

        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.IsConnected)
                {
                    await WriteMessageAsync(client, bytes, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast to client");
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                lock (_clientsLock)
                {
                    _connectedClients.Add(pipeServer);
                }

                ClientConnected?.Invoke(this, EventArgs.Empty);
                _logger.LogInformation("Client connected. Total clients: {Count}", ConnectedClients);

                _ = HandleClientAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream client, CancellationToken cancellationToken)
    {
        try
        {
            while (client.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(client, cancellationToken);
                if (message is null) break;

                var response = await ProcessMessageAsync(message, cancellationToken);
                if (response is not null)
                {
                    var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                    var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                    await WriteMessageAsync(client, responseBytes, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Client disconnected unexpectedly");
        }
        finally
        {
            lock (_clientsLock)
            {
                _connectedClients.Remove(client);
            }

            client.Dispose();
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("Client disconnected. Total clients: {Count}", ConnectedClients);
        }
    }

    private async Task<IpcMessage?> ReadMessageAsync(NamedPipeServerStream client, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        var bytesRead = await client.ReadAsync(lengthBuffer, cancellationToken);
        if (bytesRead < 4) return null;

        var length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > 1024 * 1024) return null;

        var messageBuffer = new byte[length];
        bytesRead = await client.ReadAsync(messageBuffer, cancellationToken);
        if (bytesRead < length) return null;

        var json = System.Text.Encoding.UTF8.GetString(messageBuffer);
        return JsonSerializer.Deserialize<IpcMessage>(json, JsonOptions);
    }

    private static async Task WriteMessageAsync(NamedPipeServerStream client, byte[] message, CancellationToken cancellationToken)
    {
        var lengthBytes = BitConverter.GetBytes(message.Length);
        await client.WriteAsync(lengthBytes, cancellationToken);

        await client.WriteAsync(message, cancellationToken);
        await client.FlushAsync(cancellationToken);
    }

    private async Task<IpcMessage?> ProcessMessageAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing message: {Type}", message.GetType().Name);

        return message switch
        {
            GetStatusRequest => await HandleGetStatusAsync(cancellationToken),
            GetConfigRequest => await HandleGetConfigAsync(cancellationToken),
            SaveConfigRequest req => await HandleSaveConfigAsync(req, cancellationToken),
            GetAdaptersRequest req => await HandleGetAdaptersAsync(req, cancellationToken),
            ApplyProfileRequest req => await HandleApplyProfileAsync(req, cancellationToken),
            ConnectTemporaryRequest req => await HandleConnectTemporaryAsync(req, cancellationToken),
            RevertToDefaultRequest => await HandleRevertToDefaultAsync(cancellationToken),
            DisableRequest req => await HandleDisableAsync(req, cancellationToken),
            TestProviderRequest req => await HandleTestProviderAsync(req, cancellationToken),
            FlushDnsCacheRequest => await HandleFlushDnsCacheAsync(cancellationToken),
            _ => null
        };
    }

    private Task<GetStatusResponse> HandleGetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new GetStatusResponse
        {
            Status = _dnsProxyService.Status,
            ActiveProviderId = _dnsProxyService.ActiveProvider?.Id,
            ActiveProviderName = _dnsProxyService.ActiveProvider?.Name,
            IsTemporaryConnection = _dnsProxyService.IsTemporaryConnection,
            ErrorMessage = _dnsProxyService.LastError,
            LastHealthCheck = _dnsProxyService.LastHealthCheck,
            QueriesHandled = _dnsProxyService.QueriesHandled
        });
    }

    private Task<GetConfigResponse> HandleGetConfigAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new GetConfigResponse
        {
            Success = true,
            Settings = _settingsService.Settings
        });
    }

    private async Task<SaveConfigResponse> HandleSaveConfigAsync(SaveConfigRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.UpdateAsync(request.Settings, cancellationToken);
            return new SaveConfigResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving config");
            return new SaveConfigResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<GetAdaptersResponse> HandleGetAdaptersAsync(GetAdaptersRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var adapters = await _networkAdapterService.GetAdaptersAsync(request.ConnectedOnly, cancellationToken);
            return new GetAdaptersResponse
            {
                Success = true,
                Adapters = adapters.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting adapters");
            return new GetAdaptersResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<ApplyProfileResponse> HandleApplyProfileAsync(ApplyProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var provider = _settingsService.GetProvider(request.Profile.ProviderId);
            if (provider is null)
            {
                return new ApplyProfileResponse { Success = false, ErrorMessage = "Provider not found" };
            }

            var settings = _settingsService.Settings;
            settings.DefaultProfile = request.Profile;
            settings.Enabled = request.Enable;
            await _settingsService.UpdateAsync(settings, cancellationToken);

            if (request.Enable)
            {
                await _networkAdapterService.ApplyLocalhostDnsAsync(request.Profile.AdapterIds, backupFirst: true, cancellationToken);

                if (_dnsProxyService.Status == ConnectionStatus.Inactive)
                {
                    await _dnsProxyService.StartAsync(provider, cancellationToken);
                }
                else
                {
                    await _dnsProxyService.SwitchProviderAsync(provider, isTemporary: false, cancellationToken);
                }
            }

            return new ApplyProfileResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying profile");
            return new ApplyProfileResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<ConnectTemporaryResponse> HandleConnectTemporaryAsync(ConnectTemporaryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var provider = _settingsService.GetProvider(request.ProviderId);
            if (provider is null)
            {
                return new ConnectTemporaryResponse { Success = false, ErrorMessage = "Provider not found" };
            }

            await _dnsProxyService.SwitchProviderAsync(provider, isTemporary: true, cancellationToken);
            return new ConnectTemporaryResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting temporarily");
            return new ConnectTemporaryResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<RevertToDefaultResponse> HandleRevertToDefaultAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profile = _settingsService.Settings.DefaultProfile;
            if (profile is null)
            {
                return new RevertToDefaultResponse { Success = false, ErrorMessage = "No default profile configured" };
            }

            var provider = _settingsService.GetProvider(profile.ProviderId);
            if (provider is null)
            {
                return new RevertToDefaultResponse { Success = false, ErrorMessage = "Default provider not found" };
            }

            await _dnsProxyService.SwitchProviderAsync(provider, isTemporary: false, cancellationToken);
            return new RevertToDefaultResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting to default");
            return new RevertToDefaultResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<DisableResponse> HandleDisableAsync(DisableRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _dnsProxyService.StopAsync(cancellationToken);

            if (request.RestoreOriginalDns)
            {
                await _networkAdapterService.RestoreAllFromBackupAsync(cancellationToken);
            }

            var settings = _settingsService.Settings;
            settings.Enabled = false;
            await _settingsService.UpdateAsync(settings, cancellationToken);

            return new DisableResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling");
            return new DisableResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<TestProviderResponse> HandleTestProviderAsync(TestProviderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var provider = _settingsService.GetProvider(request.ProviderId);
            if (provider is null)
            {
                return new TestProviderResponse { Success = false, ErrorMessage = "Provider not found" };
            }

            var (success, latencyMs, error) = await _dnsProxyService.TestProviderAsync(provider, request.TestDomain, cancellationToken);
            return new TestProviderResponse
            {
                Success = success,
                LatencyMs = latencyMs,
                ErrorMessage = error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing provider");
            return new TestProviderResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<FlushDnsCacheResponse> HandleFlushDnsCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _networkAdapterService.FlushDnsCacheAsync(cancellationToken);
            return new FlushDnsCacheResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing DNS cache");
            return new FlushDnsCacheResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        lock (_clientsLock)
        {
            foreach (var client in _connectedClients)
            {
                client.Dispose();
            }

            _connectedClients.Clear();
        }
    }
}
