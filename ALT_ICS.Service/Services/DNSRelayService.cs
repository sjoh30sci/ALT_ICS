using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service.Services;

/// <summary>
/// DNS relay service that forwards DNS queries from private network clients
/// to upstream DNS servers (transparent proxy - raw byte forwarding per RFC 1035).
/// </summary>
/// <remarks>
/// This service binds to UDP port 53 and forwards raw DNS query/response bytes
/// to configured upstream DNS servers without parsing DNS messages.
/// Supports primary/secondary DNS failover with 5-second timeout.
/// </remarks>
public sealed class DNSRelayService : IDisposable, IAsyncDisposable
{
    private readonly ILogger<DNSRelayService> _logger;
    private readonly NetworkConfig _config;
    private readonly IPEndPoint _listenEndPoint;
    private readonly IPEndPoint? _primaryDnsEndPoint;
    private readonly IPEndPoint? _secondaryDnsEndPoint;

    private UdpClient? _dnsSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoopTask;
    private Task? _cacheCleanupTask;
    private volatile bool _isRunning;
    private long _totalQueries;

    // Cached DNS endpoints (resolved lazily)
    private IPEndPoint? _resolvedPrimaryDns;
    private IPEndPoint? _resolvedSecondaryDns;

    /// <summary>
    /// Simple in-memory DNS response cache entry with TTL.
    /// </summary>
    private sealed class CacheEntry
    {
        public required byte[] Response { get; init; }
        public DateTime ExpiresAt { get; init; }
    }

    // Simple in-memory cache: key = query bytes hash, value = cached response with TTL
    private readonly Dictionary<int, CacheEntry> _responseCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private const int CacheTtlSeconds = 30;
    private const int MaxCacheEntries = 1000;
    private const int CacheCleanupIntervalMs = 10_000;

    /// <summary>Total DNS queries relayed since service start.</summary>
    public long TotalQueries => Interlocked.Read(ref _totalQueries);

    /// <summary>Whether the DNS relay is currently running.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSRelayService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="config">Network configuration containing upstream DNS servers.</param>
    /// <exception cref="ArgumentNullException">Thrown if logger or config is null.</exception>
    public DNSRelayService(ILogger<DNSRelayService> logger, NetworkConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _listenEndPoint = new IPEndPoint(IPAddress.Any, Constants.DnsPort);

        // Parse IP addresses immediately; hostnames will be resolved lazily in StartAsync
        _primaryDnsEndPoint = ParseIpEndPoint(_config.PrimaryDns);
        _secondaryDnsEndPoint = ParseIpEndPoint(_config.SecondaryDns);
    }

    /// <summary>
    /// Starts the DNS relay listener on UDP port 53.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for startup.</param>
    /// <returns>A task representing the asynchronous start operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if already running.</exception>
    /// <exception cref="SocketException">Thrown if port 53 cannot be bound.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay, "DNS relay is already running");
            return;
        }

        _logger.LogStateChange(nameof(DNSRelayService), "Stopped", "Starting");

        try
        {
            // Resolve hostnames to IPEndPoints if needed
            await ResolveDnsEndPointsAsync(cancellationToken).ConfigureAwait(false);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Bind UDP socket to 0.0.0.0:53
            _dnsSocket = new UdpClient(_listenEndPoint);
            _dnsSocket.Client.ReceiveTimeout = 5000;
            _dnsSocket.Client.SendTimeout = 5000;

            _isRunning = true;

            // Start the receive loop
            _receiveLoopTask = HandleIncomingQueriesAsync(_cts.Token);

            // Start cache cleanup task
            _cacheCleanupTask = RunCacheCleanupAsync(_cts.Token);

            _logger.LogInformation(CommonLogger.EventIds.DnsRelay,
                "DNS relay listening on {EndPoint}, upstream: Primary={Primary}, Secondary={Secondary}",
                _listenEndPoint, _config.PrimaryDns, _config.SecondaryDns);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _isRunning = false;
            _logger.LogError(CommonLogger.EventIds.DnsRelay, ex,
                "Port {Port} is already in use. Another DNS server may be running.", Constants.DnsPort);
            throw;
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _logger.LogServiceError(nameof(DNSRelayService), ex, "Failed to start DNS relay");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Resolves configured DNS server addresses to IPEndPoints.
    /// </summary>
    private async Task ResolveDnsEndPointsAsync(CancellationToken ct)
    {
        // Resolve primary DNS if it's a hostname
        if (_primaryDnsEndPoint == null && !string.IsNullOrWhiteSpace(_config.PrimaryDns))
        {
            _resolvedPrimaryDns = await ResolveHostnameAsync(_config.PrimaryDns, ct).ConfigureAwait(false);
        }
        else
        {
            _resolvedPrimaryDns = _primaryDnsEndPoint;
        }

        // Resolve secondary DNS if it's a hostname
        if (_secondaryDnsEndPoint == null && !string.IsNullOrWhiteSpace(_config.SecondaryDns))
        {
            _resolvedSecondaryDns = await ResolveHostnameAsync(_config.SecondaryDns, ct).ConfigureAwait(false);
        }
        else
        {
            _resolvedSecondaryDns = _secondaryDnsEndPoint;
        }

        if (_resolvedPrimaryDns == null)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay, "Could not resolve primary DNS: {PrimaryDns}", _config.PrimaryDns);
        }
        if (_resolvedSecondaryDns == null)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay, "Could not resolve secondary DNS: {SecondaryDns}", _config.SecondaryDns);
        }
    }

    /// <summary>
    /// Resolves a hostname to an IPEndPoint on DNS port.
    /// </summary>
    private static async Task<IPEndPoint?> ResolveHostnameAsync(string hostname, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                return new IPEndPoint(ipv4, Constants.DnsPort);
            }
            var ipv6 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);
            if (ipv6 != null)
            {
                return new IPEndPoint(ipv6, Constants.DnsPort);
            }
        }
        catch (Exception)
        {
            // Resolution failed, return null
        }
        return null;
    }

    /// <summary>
    /// Parses an IP address string into an IPEndPoint (synchronous, no hostname resolution).
    /// </summary>
    private static IPEndPoint? ParseIpEndPoint(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        if (IPAddress.TryParse(address, out var ip))
        {
            return new IPEndPoint(ip, Constants.DnsPort);
        }

        return null; // Hostname - will be resolved later
    }

    /// <summary>
    /// Stops the DNS relay listener and cleans up resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _logger.LogStateChange(nameof(DNSRelayService), "Running", "Stopping");
        _isRunning = false;

        try
        {
            // Cancel all operations
            _cts?.Cancel();

            // Close the UDP socket to unblock ReceiveAsync
            if (_dnsSocket != null)
            {
                try
                {
                    _dnsSocket.Close();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
                _dnsSocket = null;
            }

            // Wait for receive loop to complete
            if (_receiveLoopTask != null)
            {
                try
                {
                    await Task.WhenAny(_receiveLoopTask, Task.Delay(2000, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation during shutdown
                }
            }

            // Wait for cache cleanup to complete
            if (_cacheCleanupTask != null)
            {
                try
                {
                    await Task.WhenAny(_cacheCleanupTask, Task.Delay(500, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
            }

            // Clear cache
            await ClearCacheAsync();

            _logger.LogInformation(CommonLogger.EventIds.DnsRelay,
                "DNS relay stopped. Total queries relayed: {Count}", TotalQueries);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(DNSRelayService), ex, "Error during DNS relay shutdown");
        }
    }

    /// <summary>
    /// Main receive loop - listens for incoming DNS queries and forwards them.
    /// </summary>
    private async Task HandleIncomingQueriesAsync(CancellationToken ct)
    {
        _logger.LogDebug(CommonLogger.EventIds.DnsRelay, "DNS receive loop started");

        try
        {
            while (!ct.IsCancellationRequested && _dnsSocket != null)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _dnsSocket.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break; // Normal shutdown
                }
                catch (ObjectDisposedException)
                {
                    break; // Socket closed during shutdown
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted ||
                                                  ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break; // Socket closed during shutdown
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(CommonLogger.EventIds.DnsRelay, ex,
                        "Socket error receiving DNS query: {ErrorCode}", ex.SocketErrorCode);
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogServiceError(nameof(DNSRelayService), ex, "Error receiving DNS query");
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                // Fire-and-forget: forward query asynchronously without blocking receive loop
                _ = Task.Run(() => ForwardQueryAsync(result.Buffer, result.RemoteEndPoint, ct), ct);
            }
        }
        finally
        {
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay, "DNS receive loop stopped");
        }
    }

    /// <summary>
    /// Forwards a DNS query to upstream DNS servers with fallback and timeout.
    /// </summary>
    /// <param name="queryBytes">Raw DNS query bytes from client.</param>
    /// <param name="clientEndPoint">Client endpoint to send response back to.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ForwardQueryAsync(byte[] queryBytes, IPEndPoint clientEndPoint, CancellationToken ct)
    {
        if (queryBytes == null || queryBytes.Length == 0 || queryBytes.Length > Constants.MaxUdpPacketSize)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay,
                "Invalid DNS query size from {Client}: {Length} bytes", clientEndPoint, queryBytes?.Length ?? 0);
            return;
        }

        Interlocked.Increment(ref _totalQueries);

        // Check cache first
        var cacheKey = ComputeCacheKey(queryBytes);
        var cachedResponse = await TryGetCachedResponseAsync(cacheKey, ct).ConfigureAwait(false);
        if (cachedResponse != null)
        {
            await SendResponseAsync(cachedResponse, clientEndPoint, ct).ConfigureAwait(false);
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay,
                "Cache hit for query from {Client} (key: {Key:X})", clientEndPoint, cacheKey);
            return;
        }

        // Try primary DNS first
        byte[]? response = await TryForwardToDnsAsync(queryBytes, _resolvedPrimaryDns, _config.PrimaryDns, ct).ConfigureAwait(false);

        // Fallback to secondary DNS if primary failed
        if (response == null && _resolvedSecondaryDns != null)
        {
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay,
                "Primary DNS {Primary} failed/timeout, falling back to secondary {Secondary}",
                _config.PrimaryDns, _config.SecondaryDns);
            response = await TryForwardToDnsAsync(queryBytes, _resolvedSecondaryDns, _config.SecondaryDns, ct).ConfigureAwait(false);
        }

        if (response != null)
        {
            // Cache the response
            await TryCacheResponseAsync(cacheKey, response, ct).ConfigureAwait(false);

            // Send response back to client
            await SendResponseAsync(response, clientEndPoint, ct).ConfigureAwait(false);

            _logger.LogDebug(CommonLogger.EventIds.DnsRelay,
                "Relayed DNS query from {Client} ({QueryLen} bytes) -> {ResponseLen} bytes",
                clientEndPoint, queryBytes.Length, response.Length);
        }
        else
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay,
                "Failed to resolve DNS query from {Client} via both upstream servers", clientEndPoint);
        }
    }

    /// <summary>
    /// Attempts to forward a DNS query to a specific upstream DNS server with timeout.
    /// </summary>
    private async Task<byte[]?> TryForwardToDnsAsync(byte[] queryBytes, IPEndPoint? dnsEndPoint, string dnsName, CancellationToken ct)
    {
        if (dnsEndPoint == null)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay, "DNS server '{Dns}' is not configured", dnsName);
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5-second timeout per upstream

        try
        {
            using var upstreamSocket = new UdpClient();
            upstreamSocket.Client.SendTimeout = 5000;
            upstreamSocket.Client.ReceiveTimeout = 5000;

            await upstreamSocket.SendAsync(queryBytes, queryBytes.Length, dnsEndPoint).ConfigureAwait(false);

            var result = await upstreamSocket.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);

            if (result.Buffer.Length > 0)
            {
                _logger.LogDebug(CommonLogger.EventIds.DnsRelay,
                    "Received response from {Dns} ({Length} bytes)", dnsName, result.Buffer.Length);
                return result.Buffer;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay, "Timeout querying {Dns} (5s)", dnsName);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay, ex,
                "Socket error querying {Dns}: {ErrorCode}", dnsName, ex.SocketErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(DNSRelayService), ex, $"Error forwarding to {dnsName}");
        }

        return null;
    }

    /// <summary>
    /// Sends a DNS response back to the originating client.
    /// </summary>
    private async Task SendResponseAsync(byte[] responseBytes, IPEndPoint clientEndPoint, CancellationToken ct)
    {
        if (_dnsSocket == null || !_isRunning)
        {
            return;
        }

        try
        {
            await _dnsSocket.SendAsync(responseBytes, responseBytes.Length, clientEndPoint).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown
        }
        catch (ObjectDisposedException)
        {
            // Socket closed during shutdown
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(CommonLogger.EventIds.DnsRelay, ex,
                "Socket error sending response to {Client}: {ErrorCode}", clientEndPoint, ex.SocketErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(DNSRelayService), ex, $"Error sending DNS response to {clientEndPoint}");
        }
    }

    /// <summary>
    /// Computes a cache key from DNS query bytes using a simple hash.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeCacheKey(byte[] queryBytes)
    {
        // Simple hash of query bytes (first 12 bytes = DNS header + question section start)
        // DNS queries with same ID but different questions will have different keys
        unchecked
        {
            int hash = 17;
            int len = Math.Min(queryBytes.Length, 32); // Hash first 32 bytes max
            for (int i = 0; i < len; i++)
            {
                hash = hash * 31 + queryBytes[i];
            }
            return hash;
        }
    }

    /// <summary>
    /// Attempts to retrieve a cached response for the given query key.
    /// </summary>
    private async Task<byte[]?> TryGetCachedResponseAsync(int cacheKey, CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_responseCache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    return entry.Response;
                }
                else
                {
                    _responseCache.Remove(cacheKey);
                }
            }
            return null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Attempts to cache a DNS response.
    /// </summary>
    private async Task TryCacheResponseAsync(int cacheKey, byte[] responseBytes, CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Evict oldest entries if cache is full
            if (_responseCache.Count >= MaxCacheEntries)
            {
                var oldestKey = _responseCache.Keys.FirstOrDefault();
                if (oldestKey != 0)
                {
                    _responseCache.Remove(oldestKey);
                }
            }

            _responseCache[cacheKey] = new CacheEntry
            {
                Response = responseBytes,
                ExpiresAt = DateTime.UtcNow.AddSeconds(CacheTtlSeconds)
            };
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Periodic cache cleanup task to remove expired entries.
    /// </summary>
    private async Task RunCacheCleanupAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(CacheCleanupIntervalMs, ct).ConfigureAwait(false);

                await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var now = DateTime.UtcNow;
                    var expiredKeys = _responseCache
                        .Where(kvp => kvp.Value.ExpiresAt <= now)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expiredKeys)
                    {
                        _responseCache.Remove(key);
                    }

                    if (expiredKeys.Count > 0)
                    {
                        _logger.LogDebug(CommonLogger.EventIds.DnsRelay,
                            "DNS cache cleanup: removed {Count} expired entries, {Remaining} remaining",
                            expiredKeys.Count, _responseCache.Count);
                    }
                }
                finally
                {
                    _cacheLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(DNSRelayService), ex, "Error in cache cleanup task");
        }
    }

    /// <summary>
    /// Clears the response cache.
    /// </summary>
    private async Task ClearCacheAsync()
    {
        await _cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var count = _responseCache.Count;
            _responseCache.Clear();
            _logger.LogDebug(CommonLogger.EventIds.DnsRelay, "DNS cache cleared ({Count} entries)", count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_isRunning)
        {
            await StopAsync().ConfigureAwait(false);
        }

        _cts?.Dispose();
        _dnsSocket?.Dispose();
        _cacheLock.Dispose();
    }
}