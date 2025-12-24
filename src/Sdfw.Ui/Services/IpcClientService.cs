using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Ipc;
using Sdfw.Core.Models;

namespace Sdfw.Ui.Services;

public sealed class IpcClientService : IIpcClientService, IDisposable
{
    private const string PipeName = "SdfwServicePipe";
    private const int ConnectTimeoutMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<IpcClientService> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private NamedPipeClientStream? _pipeClient;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public IpcClientService(ILogger<IpcClientService> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => _pipeClient?.IsConnected ?? false;

#pragma warning disable CS0067
    public event EventHandler<IpcMessage>? NotificationReceived;
#pragma warning restore CS0067
    public event EventHandler<bool>? ConnectionChanged;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        try
        {
            _pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await _pipeClient.ConnectAsync(ConnectTimeoutMs, cancellationToken);

            _listenerCts = new CancellationTokenSource();
            _listenerTask = ListenForNotificationsAsync(_listenerCts.Token);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _pipeClient?.Dispose();
            _pipeClient = null;
            return false;
        }
        catch (TimeoutException)
        {
            _pipeClient?.Dispose();
            _pipeClient = null;
            return false;
        }
        catch (Exception)
        {
            _pipeClient?.Dispose();
            _pipeClient = null;
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        _listenerCts?.Cancel();

        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        _pipeClient?.Dispose();
        _pipeClient = null;
    }

    public async Task<GetStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetStatusRequest, GetStatusResponse>(new GetStatusRequest(), cancellationToken);
    }

    public async Task<GetConfigResponse?> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetConfigRequest, GetConfigResponse>(new GetConfigRequest(), cancellationToken);
    }

    public async Task<SaveConfigResponse?> SaveConfigAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<SaveConfigRequest, SaveConfigResponse>(
            new SaveConfigRequest { Settings = settings },
            cancellationToken);
    }

    public async Task<GetAdaptersResponse?> GetAdaptersAsync(bool connectedOnly = true, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetAdaptersRequest, GetAdaptersResponse>(
            new GetAdaptersRequest { ConnectedOnly = connectedOnly },
            cancellationToken);
    }

    public async Task<ApplyProfileResponse?> ApplyProfileAsync(DnsProfile profile, bool enable = true, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<ApplyProfileRequest, ApplyProfileResponse>(
            new ApplyProfileRequest { Profile = profile, Enable = enable },
            cancellationToken);
    }

    public async Task<ConnectTemporaryResponse?> ConnectTemporaryAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<ConnectTemporaryRequest, ConnectTemporaryResponse>(
            new ConnectTemporaryRequest { ProviderId = providerId },
            cancellationToken);
    }

    public async Task<RevertToDefaultResponse?> RevertToDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<RevertToDefaultRequest, RevertToDefaultResponse>(
            new RevertToDefaultRequest(),
            cancellationToken);
    }

    public async Task<DisableResponse?> DisableAsync(bool restoreOriginalDns = true, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<DisableRequest, DisableResponse>(
            new DisableRequest { RestoreOriginalDns = restoreOriginalDns },
            cancellationToken);
    }

    public async Task<TestProviderResponse?> TestProviderAsync(Guid providerId, string testDomain = "example.com", CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<TestProviderRequest, TestProviderResponse>(
            new TestProviderRequest { ProviderId = providerId, TestDomain = testDomain },
            cancellationToken);
    }

    public async Task<FlushDnsCacheResponse?> FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<FlushDnsCacheRequest, FlushDnsCacheResponse>(
            new FlushDnsCacheRequest(),
            cancellationToken);
    }

    private async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
        where TRequest : IpcMessage
        where TResponse : IpcMessage
    {
        if (!IsConnected)
        {
            var connected = await ConnectAsync(cancellationToken);
            if (!connected) return default;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize<IpcMessage>(request, JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            await _pipeClient!.WriteAsync(lengthBytes, cancellationToken);

            await _pipeClient.WriteAsync(bytes, cancellationToken);
            await _pipeClient.FlushAsync(cancellationToken);

            var responseLengthBytes = new byte[4];
            var bytesRead = await _pipeClient.ReadAsync(responseLengthBytes, cancellationToken);
            if (bytesRead < 4) return default;

            var responseLength = BitConverter.ToInt32(responseLengthBytes);
            if (responseLength <= 0 || responseLength > 1024 * 1024) return default;

            var responseBytes = new byte[responseLength];
            bytesRead = await _pipeClient.ReadAsync(responseBytes, cancellationToken);
            if (bytesRead < responseLength) return default;

            var responseJson = System.Text.Encoding.UTF8.GetString(responseBytes);
            var response = JsonSerializer.Deserialize<IpcMessage>(responseJson, JsonOptions);

            return response as TResponse;
        }
        catch (Exception)
        {
            await HandleDisconnectionAsync();
            return default;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ListenForNotificationsAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    private async Task HandleDisconnectionAsync()
    {
        _pipeClient?.Dispose();
        _pipeClient = null;
        ConnectionChanged?.Invoke(this, false);

        await Task.Delay(1000);
        await ConnectAsync();
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        _pipeClient?.Dispose();
        _sendLock.Dispose();
    }
}
