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

    private static readonly string[] BootstrapDnsServers = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    private readonly ILogger<DnsProxyService> _logger;
    private readonly ISettingsService _settingsService;
    private HttpClient _httpClient = null!;
    private SocketsHttpHandler _httpHandler = null!;

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

    private readonly Dictionary<string, IPAddress[]> _resolvedHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    public DnsProxyService(ILogger<DnsProxyService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;

        InitializeHttpClient();
    }

    private void InitializeHttpClient()
    {
        _httpHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            ConnectCallback = BootstrapConnectCallback
        };

        _httpClient = new HttpClient(_httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-message"));
    }

    private async ValueTask<Stream> BootstrapConnectCallback(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var directIp))
        {
            addresses = [directIp];
        }
        else
        {
            addresses = await ResolveHostnameAsync(host, cancellationToken);
        }

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        Socket? socket = null;
        Exception? lastException = null;

        foreach (var address in addresses)
        {
            try
            {
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                lastException = ex;
                socket?.Dispose();
                socket = null;
            }
        }

        throw lastException ?? new SocketException((int)SocketError.HostNotFound);
    }

    private async Task<IPAddress[]> ResolveHostnameAsync(string hostname, CancellationToken cancellationToken)
    {
        await _resolveLock.WaitAsync(cancellationToken);
        try
        {
            if (_resolvedHosts.TryGetValue(hostname, out var cached))
            {
                _logger.LogDebug("Using cached IP for {Hostname}: {IPs}", hostname, string.Join(", ", cached.Select(ip => ip.ToString())));
                return cached;
            }

            _logger.LogDebug("Resolving {Hostname} using bootstrap DNS servers...", hostname);
            var query = BuildDnsQuery(hostname, 1);

            foreach (var server in BootstrapDnsServers)
            {
                try
                {
                    var response = await SendBootstrapQueryAsync(server, query, cancellationToken);
                    if (response is not null)
                    {
                        var addresses = ParseDnsResponse(response);
                        if (addresses.Length > 0)
                        {
                            _resolvedHosts[hostname] = addresses;
                            _logger.LogInformation("Resolved {Hostname} to {IPs} using bootstrap DNS {Server}",
                                hostname, string.Join(", ", addresses.Select(ip => ip.ToString())), server);
                            return addresses;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Bootstrap DNS {Server} failed to resolve {Hostname}", server, hostname);
                }
            }

            try
            {
                _logger.LogDebug("Bootstrap DNS failed, trying system DNS for {Hostname}...", hostname);
                var systemAddresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);
                if (systemAddresses.Length > 0)
                {
                    _resolvedHosts[hostname] = systemAddresses;
                    _logger.LogInformation("Resolved {Hostname} to {IPs} using system DNS",
                        hostname, string.Join(", ", systemAddresses.Select(ip => ip.ToString())));
                    return systemAddresses;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "System DNS also failed to resolve {Hostname}", hostname);
            }

            _logger.LogError("Failed to resolve {Hostname} using any DNS method", hostname);
            return [];
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    private async Task<byte[]?> SendBootstrapQueryAsync(string server, byte[] query, CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse(server), DnsPort);

        client.Client.ReceiveTimeout = 2000;

        await client.SendAsync(query, query.Length, endpoint);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(2000);

        var result = await client.ReceiveAsync(cts.Token);
        return result.Buffer;
    }

    private static IPAddress[] ParseDnsResponse(byte[] response)
    {
        var addresses = new List<IPAddress>();

        if (response.Length < 12)
            return [];

        var rcode = response[3] & 0x0F;
        if (rcode != 0)
            return [];

        var answerCount = (response[6] << 8) | response[7];
        if (answerCount == 0)
            return [];

        var offset = 12;

        while (offset < response.Length && response[offset] != 0)
        {
            if ((response[offset] & 0xC0) == 0xC0)
            {
                offset += 2;
                break;
            }
            offset += response[offset] + 1;
        }
        if (response[offset] == 0)
            offset++; 

        offset += 4;

        for (var i = 0; i < answerCount && offset < response.Length; i++)
        {
            if ((response[offset] & 0xC0) == 0xC0)
            {
                offset += 2;
            }
            else
            {
                while (offset < response.Length && response[offset] != 0)
                    offset += response[offset] + 1;
                offset++;
            }

            if (offset + 10 > response.Length)
                break;

            var type = (response[offset] << 8) | response[offset + 1];
            offset += 2;
            offset += 2;
            offset += 4;
            var rdLength = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            if (type == 1 && rdLength == 4 && offset + 4 <= response.Length)
            {
                var ip = new IPAddress(new ReadOnlySpan<byte>(response, offset, 4));
                addresses.Add(ip);
            }

            offset += rdLength;
        }

        return [.. addresses];
    }

    public void ClearResolvedHostsCache()
    {
        _resolvedHosts.Clear();
        _logger.LogDebug("Cleared resolved hosts cache");
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

        using var response = await _httpClient.PostAsync(dohUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
        _httpHandler.Dispose();
        _resolveLock.Dispose();
        _resolvedHosts.Clear();
    }
}
