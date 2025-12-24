using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Net.Http;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Sdfw.Core.Models;

namespace Sdfw.Service.Services;

public sealed class DnsProxyService : IDnsProxyService, IDisposable
{
    private const int DnsPort = 53;
    private const int DnsUdpMaxSize = 512;
    private const int DnsTcpMaxSize = 65535;
    private const int DefaultTimeoutMs = 5000;

    private readonly ILogger<DnsProxyService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    private readonly object _dohClientLock = new();
    private HttpClient? _dohClient;
    private Guid? _dohClientProviderId;
    private string? _dohClientBootstrapKey;

    private UdpClient? _udpClientV4;
    private UdpClient? _udpClientV6;
    private TcpListener? _tcpListenerV4;
    private TcpListener? _tcpListenerV6;

    private CancellationTokenSource? _cts;
    private Task? _udpV4Task;
    private Task? _udpV6Task;
    private Task? _tcpV4Task;
    private Task? _tcpV6Task;

    private ConnectionStatus _status = ConnectionStatus.Inactive;
    private DnsProvider? _activeProvider;
    private DnsProvider? _defaultProvider;
    private bool _isTemporaryConnection;
    private string? _lastError;
    private long _queriesHandled;
    private DateTimeOffset? _lastHealthCheck;

    public DnsProxyService(ILogger<DnsProxyService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-message"));
    }

    public ConnectionStatus Status => _status;
    public DnsProvider? ActiveProvider => _activeProvider;
    public bool IsTemporaryConnection => _isTemporaryConnection;
    public string? LastError => _lastError;
    public long QueriesHandled => _queriesHandled;
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;

    public event EventHandler<ConnectionStatus>? StatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task StartAsync(DnsProvider provider, CancellationToken cancellationToken = default)
    {
        if (_status != ConnectionStatus.Inactive)
        {
            await StopAsync(cancellationToken);
        }

        SetStatus(ConnectionStatus.Connecting);
        _defaultProvider = provider;
        _activeProvider = provider;
        _isTemporaryConnection = false;

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _udpClientV4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, DnsPort));
            _udpClientV6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, DnsPort));

            _tcpListenerV4 = new TcpListener(IPAddress.Loopback, DnsPort);
            _tcpListenerV4.Start();

            _tcpListenerV6 = new TcpListener(IPAddress.IPv6Loopback, DnsPort);
            _tcpListenerV6.Start();

            _udpV4Task = ListenUdpAsync(_udpClientV4, "UDPv4", _cts.Token);
            _udpV6Task = ListenUdpAsync(_udpClientV6, "UDPv6", _cts.Token);
            _tcpV4Task = ListenTcpAsync(_tcpListenerV4, "TCPv4", _cts.Token);
            _tcpV6Task = ListenTcpAsync(_tcpListenerV6, "TCPv6", _cts.Token);

            _logger.LogInformation("DNS proxy started on localhost:53");

            SetStatus(ConnectionStatus.Testing);
            var (success, _, error) = await TestProviderAsync(provider, "example.com", cancellationToken);

            if (success)
            {
                SetStatus(ConnectionStatus.Connected);
                _lastHealthCheck = DateTimeOffset.UtcNow;
            }
            else
            {
                _lastError = error;
                SetStatus(ConnectionStatus.Error);
                ErrorOccurred?.Invoke(this, error ?? "Connection test failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DNS proxy");
            _lastError = ex.Message;
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DNS proxy...");

        _cts?.Cancel();

        _udpClientV4?.Close();
        _udpClientV6?.Close();
        _tcpListenerV4?.Stop();
        _tcpListenerV6?.Stop();

        var tasks = new[] { _udpV4Task, _udpV6Task, _tcpV4Task, _tcpV6Task }
            .Where(t => t is not null)
            .Cast<Task>()
            .ToArray();

        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Tasks did not complete within timeout");
            }
        }

        _udpClientV4?.Dispose();
        _udpClientV6?.Dispose();
        _udpClientV4 = null;
        _udpClientV6 = null;
        _tcpListenerV4 = null;
        _tcpListenerV6 = null;

        _activeProvider = null;
        _defaultProvider = null;
        _isTemporaryConnection = false;

        SetStatus(ConnectionStatus.Inactive);
        _logger.LogInformation("DNS proxy stopped");
    }

    public async Task SwitchProviderAsync(DnsProvider provider, bool isTemporary = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Switching to provider {Name} (temporary: {IsTemp})", provider.Name, isTemporary);

        if (!isTemporary)
        {
            _defaultProvider = provider;
        }

        _activeProvider = provider;
        _isTemporaryConnection = isTemporary;

        SetStatus(ConnectionStatus.Testing);
        var (success, _, error) = await TestProviderAsync(provider, "example.com", cancellationToken);

        if (success)
        {
            SetStatus(ConnectionStatus.Connected);
            _lastHealthCheck = DateTimeOffset.UtcNow;
        }
        else
        {
            _lastError = error;
            SetStatus(ConnectionStatus.Error);
            ErrorOccurred?.Invoke(this, error ?? "Provider test failed");
        }
    }

    public async Task<(bool Success, double? LatencyMs, string? Error)> TestProviderAsync(
        DnsProvider provider,
        string testDomain = "example.com",
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var query = BuildDnsQuery(testDomain, 1);

            byte[]? response;

            if (provider.Type == DnsProviderType.DoH)
            {
                response = await SendDoHQueryAsync(provider, query, cancellationToken);
            }
            else
            {
                var server = provider.PrimaryIpv4 ?? provider.PrimaryIpv6;
                if (string.IsNullOrEmpty(server))
                {
                    return (false, null, "No DNS server configured");
                }

                response = await SendUdpQueryAsync(server, query, cancellationToken);
            }

            sw.Stop();

            if (response is null || response.Length < 12)
            {
                return (false, null, "Invalid response");
            }

            var rcode = response[3] & 0x0F;
            if (rcode != 0)
            {
                return (false, sw.Elapsed.TotalMilliseconds, $"DNS error code: {rcode}");
            }

            return (true, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Provider test failed");
            return (false, null, ex.Message);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is null)
        {
            return false;
        }

        var (success, _, _) = await TestProviderAsync(_activeProvider, "example.com", cancellationToken);
        _lastHealthCheck = DateTimeOffset.UtcNow;
        return success;
    }

    private async Task ListenUdpAsync(UdpClient client, string name, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting {Name} listener", name);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(cancellationToken);
                _ = HandleUdpQueryAsync(client, result.RemoteEndPoint, result.Buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {Name} listener", name);
            }
        }

        _logger.LogDebug("{Name} listener stopped", name);
    }

    private async Task ListenTcpAsync(TcpListener listener, string name, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting {Name} listener", name);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleTcpClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {Name} listener", name);
            }
        }

        _logger.LogDebug("{Name} listener stopped", name);
    }

    private async Task HandleUdpQueryAsync(UdpClient client, IPEndPoint remoteEp, byte[] query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ForwardQueryAsync(query, cancellationToken);
            if (response is not null)
            {
                await client.SendAsync(response, response.Length, remoteEp);
                Interlocked.Increment(ref _queriesHandled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UDP query");
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                var lengthBuffer = new byte[2];
                var bytesRead = await stream.ReadAsync(lengthBuffer, cancellationToken);
                if (bytesRead < 2) return;

                var length = (lengthBuffer[0] << 8) | lengthBuffer[1];
                if (length > DnsTcpMaxSize) return;

                var query = new byte[length];
                bytesRead = await stream.ReadAsync(query, cancellationToken);
                if (bytesRead < length) return;

                var response = await ForwardQueryAsync(query, cancellationToken);
                if (response is null) return;

                var responseLength = new byte[2];
                responseLength[0] = (byte)(response.Length >> 8);
                responseLength[1] = (byte)(response.Length & 0xFF);

                await stream.WriteAsync(responseLength, cancellationToken);
                await stream.WriteAsync(response, cancellationToken);

                Interlocked.Increment(ref _queriesHandled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling TCP client");
            }
        }
    }

    private async Task<byte[]?> ForwardQueryAsync(byte[] query, CancellationToken cancellationToken)
    {
        if (_activeProvider is null)
        {
            return null;
        }

        try
        {
            return _activeProvider.Type == DnsProviderType.DoH
                ? await SendDoHQueryAsync(_activeProvider, query, cancellationToken)
                : await SendStandardDnsQueryAsync(_activeProvider, query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding query");
            return null;
        }
    }

    private async Task<byte[]?> SendStandardDnsQueryAsync(DnsProvider provider, byte[] query, CancellationToken cancellationToken)
    {
        var servers = new List<string>();
        if (!string.IsNullOrEmpty(provider.PrimaryIpv4)) servers.Add(provider.PrimaryIpv4);
        if (!string.IsNullOrEmpty(provider.SecondaryIpv4)) servers.Add(provider.SecondaryIpv4);
        if (!string.IsNullOrEmpty(provider.PrimaryIpv6)) servers.Add(provider.PrimaryIpv6);
        if (!string.IsNullOrEmpty(provider.SecondaryIpv6)) servers.Add(provider.SecondaryIpv6);

        foreach (var server in servers)
        {
            try
            {
                var response = await SendUdpQueryAsync(server, query, cancellationToken);
                if (response is not null)
                {
                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query server {Server}", server);
            }
        }

        return null;
    }

    private async Task<byte[]?> SendUdpQueryAsync(string server, byte[] query, CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse(server), DnsPort);

        client.Client.ReceiveTimeout = DefaultTimeoutMs;

        await client.SendAsync(query, query.Length, endpoint);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeoutMs);

        var result = await client.ReceiveAsync(cts.Token);
        return result.Buffer;
    }

    private async Task<byte[]?> SendDoHQueryAsync(DnsProvider provider, byte[] query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(provider.DohUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(provider.DohUrl, UriKind.Absolute, out var dohUri))
        {
            return null;
        }

        using var content = new ByteArrayContent(query);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

        var client = GetDohHttpClient(provider);
        using var response = await client.PostAsync(dohUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private HttpClient GetDohHttpClient(DnsProvider provider)
    {
        if (provider.BootstrapIps is not { Count: > 0 })
        {
            return _httpClient;
        }

        var bootstrapKey = string.Join("|", provider.BootstrapIps);

        lock (_dohClientLock)
        {
            if (_dohClient is not null && _dohClientProviderId == provider.Id && _dohClientBootstrapKey == bootstrapKey)
            {
                return _dohClient;
            }

            _dohClient?.Dispose();

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(10),
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                },
                ConnectCallback = async (context, token) =>
                {
                    var host = context.DnsEndPoint.Host;
                    var port = context.DnsEndPoint.Port;

                    foreach (var ipStr in provider.BootstrapIps)
                    {
                        if (!IPAddress.TryParse(ipStr, out var ip))
                            continue;

                        try
                        {
                            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                            await socket.ConnectAsync(new IPEndPoint(ip, port), token);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Bootstrap IP connect failed: {Ip} ({Host})", ipStr, host);
                        }
                    }

                    var addrs = Dns.GetHostAddresses(host);
                    foreach (var addr in addrs)
                    {
                        try
                        {
                            var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                            socket.Connect(new IPEndPoint(addr, port));
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                        }
                    }

                    throw new HttpRequestException($"Failed to connect to DoH host {host} using bootstrap IPs.");
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-message"));

            _dohClient = client;
            _dohClientProviderId = provider.Id;
            _dohClientBootstrapKey = bootstrapKey;

            return client;
        }
    }

    private static byte[] BuildDnsQuery(string domain, ushort type)
    {
        var random = new Random();
        var id = (ushort)random.Next(0, ushort.MaxValue);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(SwapEndian(id));
        writer.Write(SwapEndian((ushort)0x0100));
        writer.Write(SwapEndian((ushort)1));
        writer.Write(SwapEndian((ushort)0));
        writer.Write(SwapEndian((ushort)0));
        writer.Write(SwapEndian((ushort)0));

        foreach (var label in domain.Split('.'))
        {
            writer.Write((byte)label.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes(label));
        }
        writer.Write((byte)0);

        writer.Write(SwapEndian(type));
        writer.Write(SwapEndian((ushort)1));

        return ms.ToArray();
    }

    private static ushort SwapEndian(ushort value)
    {
        return (ushort)((value << 8) | (value >> 8));
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (_status == status) return;

        var previous = _status;
        _status = status;

        _logger.LogInformation("Status changed: {Previous} -> {New}", previous, status);
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClientV4?.Dispose();
        _udpClientV6?.Dispose();
        _httpClient.Dispose();
        _dohClient?.Dispose();
    }
}
