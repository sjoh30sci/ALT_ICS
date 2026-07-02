using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace ALT_ICS.Service.Services;

/// <summary>
/// Implements the NAT translation engine for ALT_ICS.
/// Manages session tracking, port allocation, and packet translation for IPv4 TCP/UDP traffic.
/// </summary>
public class NATConnectionService : INATService, ISessionTable, IDisposable
{
    private readonly ILogger<NATConnectionService> _logger;
    private readonly NetworkConfig _config;
    private readonly object _configLock = new();
    private readonly Timer _cleanupTimer;
    private readonly CancellationTokenSource _cleanupCts = new();

    // Session tracking
    private readonly ConcurrentDictionary<string, NatSession> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _inboundIndex = new(); // key: "publicPort:destIp:destPort:proto" -> sessionId

    // Port allocation
    private const int MinEphemeralPort = 49152;
    private const int MaxEphemeralPort = 65535;
    private readonly bool[] _portInUse = new bool[MaxEphemeralPort - MinEphemeralPort + 1];
    private int _nextPortIndex = 0;
    private readonly object _portLock = new();

    // Interface configuration
    private string _publicInterface = string.Empty;
    private string _privateInterface = string.Empty;
    private IPAddress? _publicIp;
    private IPAddress? _privateIp;
    private IPAddress? _privateSubnet;
    private IPAddress? _privateMask;

    // Raw socket handles (for future packet capture implementation)
    private NativeMethods.SafeSocketHandle? _publicSocket;
    private NativeMethods.SafeSocketHandle? _privateSocket;

    // State
    private volatile bool _isRunning;
    private volatile bool _disposed;

    // Statistics counters (thread-safe via Interlocked)
    private long _totalBytesForwarded;
    private long _packetsTranslated;
    private long _packetsDropped;
    private long _bytesInCurrentWindow;
    private DateTime _windowStart = DateTime.UtcNow;

    // Session timeout configuration
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromSeconds(Constants.DefaultNatSessionTimeoutSeconds);

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public string PublicInterface => _publicInterface;

    /// <inheritdoc />
    public string PrivateInterface => _privateInterface;

    /// <summary>
    /// Initializes a new instance of the <see cref="NATConnectionService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="config">Network configuration options.</param>
    public NATConnectionService(ILogger<NATConnectionService> logger, IOptions<NetworkConfig> config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

        // Start background cleanup timer (every 30 seconds)
        _cleanupTimer = new Timer(CleanupExpiredSessionsCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogDebug("NAT engine already running");
            return;
        }

        _logger.LogStateChange(nameof(NATConnectionService), "Stopped", "Starting");

        try
        {
            // Validate interfaces are configured
            if (string.IsNullOrWhiteSpace(_publicInterface) || string.IsNullOrWhiteSpace(_privateInterface))
            {
                throw new InvalidOperationException("Public and private interfaces must be configured before starting. Call ConfigureInterfacesAsync first.");
            }

            // Resolve interface IP addresses
            await ResolveInterfaceAddressesAsync(cancellationToken);

            // Initialize raw sockets (placeholder for full packet capture implementation)
            await InitializeRawSocketsAsync(cancellationToken);

            // Enable IP forwarding on Windows (requires admin)
            EnableIpForwarding();

            _isRunning = true;
            _windowStart = DateTime.UtcNow;

            _logger.LogInformation(CommonLogger.EventIds.NatEngine,
                "NAT engine started on public={Public} ({PublicIp}) private={Private} ({PrivateIp})",
                _publicInterface, _publicIp, _privateInterface, _privateIp);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Failed to start NAT engine");
            _isRunning = false;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            _logger.LogDebug("NAT engine already stopped");
            return;
        }

        _logger.LogStateChange(nameof(NATConnectionService), "Running", "Stopping");

        try
        {
            _isRunning = false;

            // Stop cleanup timer
            _cleanupCts.Cancel();
            await _cleanupTimer!.DisposeAsync();

            // Close raw sockets
            CloseRawSockets();

            // Clear all sessions
            ClearSessions();

            // Release all allocated ports
            lock (_portLock)
            {
                Array.Clear(_portInUse, 0, _portInUse.Length);
                _nextPortIndex = 0;
            }

            _logger.LogInformation(CommonLogger.EventIds.NatEngine, "NAT engine stopped");
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Error during NAT engine shutdown");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<NATStats> GetStatsAsync()
    {
        // Calculate throughput (bytes per second over the current window)
        var now = DateTime.UtcNow;
        var elapsed = (now - _windowStart).TotalSeconds;
        long throughput = 0;
        if (elapsed > 0)
        {
            throughput = (long)(Interlocked.Read(ref _bytesInCurrentWindow) / elapsed);
        }

        // Reset window if it's been more than 10 seconds
        if (elapsed > 10)
        {
            Interlocked.Exchange(ref _bytesInCurrentWindow, 0);
            _windowStart = now;
        }

        return Task.FromResult(new NATStats
        {
            ActiveSessions = _sessions.Count,
            TotalBytesForwarded = Interlocked.Read(ref _totalBytesForwarded),
            ThroughputBps = throughput,
            PacketsTranslated = Interlocked.Read(ref _packetsTranslated),
            PacketsDropped = Interlocked.Read(ref _packetsDropped),
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task ConfigureInterfacesAsync(string publicInterface, string privateInterface)
    {
        if (string.IsNullOrWhiteSpace(publicInterface))
            throw new ArgumentException("Public interface name cannot be empty", nameof(publicInterface));
        if (string.IsNullOrWhiteSpace(privateInterface))
            throw new ArgumentException("Private interface name cannot be empty", nameof(privateInterface));
        if (publicInterface.Equals(privateInterface, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Public and private interfaces must be different");

        lock (_configLock)
        {
            _logger.LogConfigChange("PublicInterface", _publicInterface, publicInterface);
            _logger.LogConfigChange("PrivateInterface", _privateInterface, privateInterface);

            _publicInterface = publicInterface;
            _privateInterface = privateInterface;
        }

        // If already running, re-resolve addresses
        if (_isRunning)
        {
            await ResolveInterfaceAddressesAsync(CancellationToken.None);
        }
    }

    // ==================== ISessionTable Implementation ====================

    /// <inheritdoc />
    public int Count => _sessions.Count;

    /// <inheritdoc />
    public ITranslationEntry? FindOrCreateOutboundSession(
        string sourceIp,
        int sourcePort,
        string destinationIp,
        int destinationPort,
        string protocol,
        string translatedIp,
        int translatedPort)
    {
        if (!_isRunning)
            return null;

        var sessionId = GenerateSessionId(sourceIp, sourcePort, destinationIp, destinationPort, protocol);

        // Try to get existing session
        if (_sessions.TryGetValue(sessionId, out var existingSession))
        {
            existingSession.UpdateActivity(0);
            return existingSession;
        }

        // Allocate a translated port if not provided (0 means auto-allocate)
        int actualTranslatedPort = translatedPort;
        if (actualTranslatedPort == 0)
        {
            actualTranslatedPort = AllocatePort();
            if (actualTranslatedPort == -1)
            {
                _logger.LogWarning(CommonLogger.EventIds.NatEngine,
                    "Failed to allocate translated port for session {SessionId}", sessionId);
                Interlocked.Increment(ref _packetsDropped);
                return null;
            }
        }

        // Create new session
        var session = new NatSession
        {
            SessionId = sessionId,
            OriginalSourceIp = sourceIp,
            OriginalSourcePort = sourcePort,
            TranslatedSourceIp = translatedIp,
            TranslatedSourcePort = actualTranslatedPort,
            DestinationIp = destinationIp,
            DestinationPort = destinationPort,
            Protocol = protocol.ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            IsInbound = false
        };

        if (_sessions.TryAdd(sessionId, session))
        {
            // Add to inbound index for reverse lookup
            var inboundKey = GenerateInboundKey(actualTranslatedPort, destinationIp, destinationPort, protocol);
            _inboundIndex.TryAdd(inboundKey, string.Empty); // Value unused, key maps to sessionId via session

            _logger.LogDebug(CommonLogger.EventIds.NatEngine,
                "Created outbound session: {SessionId} -> {TranslatedIp}:{TranslatedPort}",
                sessionId, translatedIp, actualTranslatedPort);

            return session;
        }

        // Race condition - another thread created it
        if (_sessions.TryGetValue(sessionId, out var raceSession))
        {
            // Release the port we allocated since we didn't use it
            ReleasePort(actualTranslatedPort);
            return raceSession;
        }

        ReleasePort(actualTranslatedPort);
        return null;
    }

    /// <inheritdoc />
    public ITranslationEntry? FindInboundSession(
        string destinationIp,
        int destinationPort,
        string sourceIp,
        int sourcePort,
        string protocol)
    {
        if (!_isRunning)
            return null;

        // For inbound, we look up by the translated (public) port and destination
        // The key format: "publicPort:destIp:destPort:proto"
        var inboundKey = GenerateInboundKey(destinationPort, destinationIp, sourcePort, protocol);

        if (_inboundIndex.TryGetValue(inboundKey, out _))
        {
            // Find the session that matches this mapping
            // We need to search sessions for the one with matching translated port and destination
            foreach (var session in _sessions.Values)
            {
                if (!session.IsInbound &&
                    session.TranslatedSourcePort == destinationPort &&
                    session.DestinationIp.Equals(destinationIp, StringComparison.OrdinalIgnoreCase) &&
                    session.DestinationPort == sourcePort &&
                    session.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
                {
                    session.UpdateActivity(0);
                    return session;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            // Remove from inbound index
            var inboundKey = GenerateInboundKey(session.TranslatedSourcePort, session.DestinationIp, session.DestinationPort, session.Protocol);
            _inboundIndex.TryRemove(inboundKey, out _);

            // Release the translated port
            ReleasePort(session.TranslatedSourcePort);

            _logger.LogDebug(CommonLogger.EventIds.NatEngine, "Removed session: {SessionId}", sessionId);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public int RemoveExpiredSessions(TimeSpan timeout)
    {
        var removed = 0;
        var expiredSessions = new List<string>();

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.IsExpired(timeout))
            {
                expiredSessions.Add(kvp.Key);
            }
        }

        foreach (var sessionId in expiredSessions)
        {
            if (RemoveSession(sessionId))
                removed++;
        }

        if (removed > 0)
        {
            _logger.LogDebug(CommonLogger.EventIds.NatEngine,
                "Cleaned up {Count} expired NAT sessions", removed);
        }

        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ITranslationEntry> GetAllSessions()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public void UpdateActivity(string sessionId, long bytesTransferred = 0)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.UpdateActivity(bytesTransferred);

            // Update global counters
            if (bytesTransferred > 0)
            {
                Interlocked.Add(ref _totalBytesForwarded, bytesTransferred);
                Interlocked.Add(ref _bytesInCurrentWindow, bytesTransferred);
                Interlocked.Increment(ref _packetsTranslated);
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        ClearSessions();
    }

    // ==================== Packet Processing Framework ====================

    /// <summary>
    /// Processes an outbound packet from the private network.
    /// Rewrites source IP and port to the public interface values.
    /// </summary>
    /// <param name="packet">Raw packet data including IP header.</param>
    /// <param name="sourceIp">Original source IP (private).</param>
    /// <param name="sourcePort">Original source port.</param>
    /// <param name="destIp">Destination IP.</param>
    /// <param name="destPort">Destination port.</param>
    /// <param name="protocol">Protocol ("TCP" or "UDP").</param>
    /// <returns>True if packet was translated and should be forwarded; false to drop.</returns>
    public bool ProcessOutboundPacket(
        byte[] packet,
        string sourceIp,
        int sourcePort,
        string destIp,
        int destPort,
        string protocol)
    {
        if (!_isRunning)
            return false;

        try
        {
            // Find or create session
            var session = FindOrCreateOutboundSession(
                sourceIp, sourcePort, destIp, destPort, protocol,
                _publicIp?.ToString() ?? string.Empty, 0);

            if (session == null)
            {
                Interlocked.Increment(ref _packetsDropped);
                return false;
            }

            // TODO: Actual packet rewrite logic:
            // 1. Parse IP header (verify checksum, TTL, etc.)
            // 2. Parse TCP/UDP header
            // 3. Rewrite source IP to _publicIp
            // 4. Rewrite source port to session.TranslatedSourcePort
            // 5. Recalculate IP header checksum
            // 6. Recalculate TCP/UDP checksum (pseudo-header changed)
            // 7. Send via _publicSocket

            // For now, simulate successful translation
            session.LastActivity = DateTime.UtcNow;
            session.BytesTransferred += packet.Length;
            session.PacketsTransmitted++;

            _logger.LogTrace(CommonLogger.EventIds.NatEngine,
                "Outbound: {SrcIp}:{SrcPort} -> {DstIp}:{DstPort} [{Proto}] mapped to {TranslatedIp}:{TranslatedPort}",
                sourceIp, sourcePort, destIp, destPort, protocol,
                session.TranslatedSourceIp, session.TranslatedSourcePort);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Error processing outbound packet");
            Interlocked.Increment(ref _packetsDropped);
            return false;
        }
    }

    /// <summary>
    /// Processes an inbound packet from the public network.
    /// Rewrites destination IP and port back to the original private values.
    /// </summary>
    /// <param name="packet">Raw packet data including IP header.</param>
    /// <param name="sourceIp">Source IP (public internet).</param>
    /// <param name="sourcePort">Source port.</param>
    /// <param name="destIp">Destination IP (public interface).</param>
    /// <param name="destPort">Destination port (translated port).</param>
    /// <param name="protocol">Protocol ("TCP" or "UDP").</param>
    /// <returns>True if packet was translated and should be forwarded; false to drop.</returns>
    public bool ProcessInboundPacket(
        byte[] packet,
        string sourceIp,
        int sourcePort,
        string destIp,
        int destPort,
        string protocol)
    {
        if (!_isRunning)
            return false;

        try
        {
            // Reverse lookup: find session by translated port and destination
            var session = FindInboundSession(destIp, destPort, sourceIp, sourcePort, protocol);

            if (session == null)
            {
                // No mapping exists - could be unsolicited inbound (drop or handle via port forwarding)
                Interlocked.Increment(ref _packetsDropped);
                _logger.LogTrace(CommonLogger.EventIds.NatEngine,
                    "Inbound packet dropped - no mapping: {SrcIp}:{SrcPort} -> {DstIp}:{DstPort} [{Proto}]",
                    sourceIp, sourcePort, destIp, destPort, protocol);
                return false;
            }

            // TODO: Actual packet rewrite logic:
            // 1. Parse IP header
            // 2. Parse TCP/UDP header
            // 3. Rewrite destination IP to session.OriginalSourceIp
            // 4. Rewrite destination port to session.OriginalSourcePort
            // 5. Recalculate IP header checksum
            // 6. Recalculate TCP/UDP checksum
            // 7. Send via _privateSocket

            session.LastActivity = DateTime.UtcNow;
            session.BytesTransferred += packet.Length;
            session.PacketsTransmitted++;

            _logger.LogTrace(CommonLogger.EventIds.NatEngine,
                "Inbound: {SrcIp}:{SrcPort} -> {DstIp}:{DstPort} [{Proto}] mapped to {OriginalIp}:{OriginalPort}",
                sourceIp, sourcePort, destIp, destPort, protocol,
                session.OriginalSourceIp, session.OriginalSourcePort);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Error processing inbound packet");
            Interlocked.Increment(ref _packetsDropped);
            return false;
        }
    }

    // ==================== Private Implementation ====================

    /// <summary>
    /// Resolves the IP addresses for the configured interfaces.
    /// </summary>
    private async Task ResolveInterfaceAddressesAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            lock (_configLock)
            {
                try
                {
                    _publicIp = GetInterfaceIpv4Address(_publicInterface);
                    _privateIp = GetInterfaceIpv4Address(_privateInterface);

                    if (_publicIp == null)
                        throw new InvalidOperationException($"Could not resolve IPv4 address for public interface '{_publicInterface}'");
                    if (_privateIp == null)
                        throw new InvalidOperationException($"Could not resolve IPv4 address for private interface '{_privateInterface}'");

                    // Parse subnet configuration
                    if (IPAddress.TryParse(_config.PrivateSubnet, out var subnet))
                        _privateSubnet = subnet;
                    if (IPAddress.TryParse(_config.PrivateSubnetMask, out var mask))
                        _privateMask = mask;

                    _logger.LogInformation(CommonLogger.EventIds.NatEngine,
                        "Resolved interfaces: Public={PublicIp} ({PublicIf}), Private={PrivateIp} ({PrivateIf})",
                        _publicIp, _publicInterface, _privateIp, _privateInterface);
                }
                catch (Exception ex)
                {
                    _logger.LogServiceError(nameof(NATConnectionService), ex, "Failed to resolve interface addresses");
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the first IPv4 address assigned to the specified network interface.
    /// </summary>
    private static IPAddress? GetInterfaceIpv4Address(string interfaceName)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var ni = interfaces.FirstOrDefault(i =>
                i.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));

            if (ni == null)
                return null;

            var ipProps = ni.GetIPProperties();
            return ipProps.UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => u.Address)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Initializes raw sockets for packet capture on both interfaces.
    /// </summary>
    private async Task InitializeRawSocketsAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                // Create raw socket for public interface
                _publicSocket = CreateRawSocket(_publicIp!);
                if (_publicSocket != null && !_publicSocket.IsInvalid)
                {
                    BindSocketToInterface(_publicSocket, _publicIp!);
                    EnablePromiscuousMode(_publicSocket, true);
                }

                // Create raw socket for private interface
                _privateSocket = CreateRawSocket(_privateIp!);
                if (_privateSocket != null && !_privateSocket.IsInvalid)
                {
                    BindSocketToInterface(_privateSocket, _privateIp!);
                    EnablePromiscuousMode(_privateSocket, true);
                }

                _logger.LogInformation(CommonLogger.EventIds.NatEngine, "Raw sockets initialized for packet capture");
            }
            catch (Exception ex)
            {
                _logger.LogServiceError(nameof(NATConnectionService), ex, "Failed to initialize raw sockets (requires Administrator privileges)");
                // Don't throw - NAT can work in user-mode with packet filtering APIs
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a raw socket for IPv4 packet capture.
    /// </summary>
    private static NativeMethods.SafeSocketHandle? CreateRawSocket(IPAddress bindAddress)
    {
        try
        {
            var socket = NativeMethods.WSASocketW(
                AddressFamily.InterNetwork,
                SocketType.Raw,
                ProtocolType.IP,
                IntPtr.Zero,
                0,
                SocketFlags.None);

            if (socket.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(error, "WSASocketW failed");
            }

            // Set IP_HDRINCL to include IP header in send/recv
            int optVal = 1;
            int result = NativeMethods.setsockopt(socket, NativeMethods.IPPROTO_IP, NativeMethods.IP_HDRINCL, ref optVal, sizeof(int));
            if (result != 0)
            {
                var error = Marshal.GetLastWin32Error();
                socket.Dispose();
                throw new System.ComponentModel.Win32Exception(error, "setsockopt IP_HDRINCL failed");
            }

            return socket;
        }
        catch (Exception ex)
        {
            // Log and return null - caller handles
            System.Diagnostics.Debug.WriteLine($"CreateRawSocket failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Binds a raw socket to a specific local IP address.
    /// </summary>
    private static void BindSocketToInterface(NativeMethods.SafeSocketHandle socket, IPAddress address)
    {
        var addrBytes = address.GetAddressBytes();
        var addr = new NativeMethods.sockaddr_in
        {
            sin_family = (short)AddressFamily.InterNetwork,
            sin_port = 0,
            sin_addr = new NativeMethods.in_addr
            {
                S_addr = ((uint)addrBytes[0] << 24) | ((uint)addrBytes[1] << 16) | ((uint)addrBytes[2] << 8) | addrBytes[3]
            }
        };

        int result = NativeMethods.bind(socket, ref addr, Marshal.SizeOf<NativeMethods.sockaddr_in>());
        if (result != 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error, "bind failed");
        }
    }

    /// <summary>
    /// Enables or disables promiscuous mode (SIO_RCVALL) on a raw socket.
    /// </summary>
    private static void EnablePromiscuousMode(NativeMethods.SafeSocketHandle socket, bool enable)
    {
        int cmd = enable ? NativeMethods.RCVALL_ON : NativeMethods.RCVALL_OFF;
        int result = NativeMethods.ioctlsocket(socket, NativeMethods.SIO_RCVALL, ref cmd);
        if (result != 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error, "ioctlsocket SIO_RCVALL failed");
        }
    }

    /// <summary>
    /// Closes raw sockets.
    /// </summary>
    private void CloseRawSockets()
    {
        _publicSocket?.Dispose();
        _publicSocket = null;
        _privateSocket?.Dispose();
        _privateSocket = null;
    }

    /// <summary>
    /// Enables IP forwarding (routing) on the system via IP Helper API.
    /// Requires Administrator privileges.
    /// </summary>
    private void EnableIpForwarding()
    {
        try
        {
            // Note: On Windows, IP forwarding is typically enabled via registry:
            // HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\IPEnableRouter = 1
            // The SetIpForwardEntry API modifies the routing table but doesn't persist the global setting.
            // For a service, we should use the registry or netsh.
            // This is a placeholder for the actual implementation.

            _logger.LogInformation(CommonLogger.EventIds.NatEngine,
                "IP forwarding enable requested (requires admin + registry change for persistence)");
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Failed to enable IP forwarding");
        }
    }

    /// <summary>
    /// Generates a unique session ID from the 5-tuple.
    /// Format: "srcIP:srcPort-dstIP:dstPort-protocol"
    /// </summary>
    private static string GenerateSessionId(string srcIp, int srcPort, string dstIp, int dstPort, string protocol)
    {
        return $"{srcIp}:{srcPort}-{dstIp}:{dstPort}-{protocol.ToUpperInvariant()}";
    }

    /// <summary>
    /// Generates an inbound lookup key from translated port and destination.
    /// Format: "publicPort:destIp:destPort:proto"
    /// </summary>
    private static string GenerateInboundKey(int publicPort, string destIp, int destPort, string protocol)
    {
        return $"{publicPort}:{destIp}:{destPort}:{protocol.ToUpperInvariant()}";
    }

    /// <summary>
    /// Allocates the next available port from the ephemeral range (49152-65535).
    /// </summary>
    private int AllocatePort()
    {
        lock (_portLock)
        {
            int startIndex = _nextPortIndex;
            int arrayLength = _portInUse.Length;

            do
            {
                if (!_portInUse[_nextPortIndex])
                {
                    _portInUse[_nextPortIndex] = true;
                    int allocatedPort = MinEphemeralPort + _nextPortIndex;
                    _nextPortIndex = (_nextPortIndex + 1) % arrayLength;
                    return allocatedPort;
                }
                _nextPortIndex = (_nextPortIndex + 1) % arrayLength;
            }
            while (_nextPortIndex != startIndex);

            return -1; // No ports available
        }
    }

    /// <summary>
    /// Releases a previously allocated port back to the pool.
    /// </summary>
    private void ReleasePort(int port)
    {
        if (port < MinEphemeralPort || port > MaxEphemeralPort)
            return;

        lock (_portLock)
        {
            int index = port - MinEphemeralPort;
            if (index >= 0 && index < _portInUse.Length)
            {
                _portInUse[index] = false;
            }
        }
    }

    /// <summary>
    /// Timer callback for periodic session cleanup.
    /// </summary>
    private void CleanupExpiredSessionsCallback(object? state)
    {
        if (!_isRunning || _disposed)
            return;

        try
        {
            RemoveExpiredSessions(_sessionTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(NATConnectionService), ex, "Error during session cleanup");
        }
    }

    /// <summary>
    /// Clears all sessions and releases associated resources.
    /// </summary>
    private void ClearSessions()
    {
        foreach (var session in _sessions.Values)
        {
            ReleasePort(session.TranslatedSourcePort);
        }
        _sessions.Clear();
        _inboundIndex.Clear();
    }

    /// <summary>
    /// Disposes managed and unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore exceptions during disposal
        }

        _cleanupTimer?.Dispose();
        _cleanupCts.Dispose();
        CloseRawSockets();
    }
}

/// <summary>
/// Represents a single NAT translation session (implements ITranslationEntry).
/// </summary>
internal sealed class NatSession : ITranslationEntry
{
    /// <inheritdoc />
    public string SessionId { get; set; } = string.Empty;

    /// <inheritdoc />
    public string OriginalSourceIp { get; set; } = string.Empty;

    /// <inheritdoc />
    public int OriginalSourcePort { get; set; }

    /// <inheritdoc />
    public string TranslatedSourceIp { get; set; } = string.Empty;

    /// <inheritdoc />
    public int TranslatedSourcePort { get; set; }

    /// <inheritdoc />
    public string DestinationIp { get; set; } = string.Empty;

    /// <inheritdoc />
    public int DestinationPort { get; set; }

    /// <inheritdoc />
    public string Protocol { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTime LastActivity { get; set; }

    /// <inheritdoc />
    public long BytesTransferred { get; set; }

    /// <inheritdoc />
    public long PacketsTransmitted { get; set; }

    /// <inheritdoc />
    public bool IsInbound { get; set; }

    /// <summary>
    /// Updates the last activity timestamp and counters.
    /// </summary>
    /// <param name="bytesTransferred">Bytes transferred in this update.</param>
    public void UpdateActivity(long bytesTransferred = 0)
    {
        LastActivity = DateTime.UtcNow;
        if (bytesTransferred > 0)
        {
            BytesTransferred += bytesTransferred;
            PacketsTransmitted++;
        }
    }

    /// <inheritdoc />
    public bool IsExpired(TimeSpan timeout)
    {
        return DateTime.UtcNow - LastActivity > timeout;
    }
}

// Extension method for NetworkConfig to access private subnet settings
internal static class NetworkConfigExtensions
{
    public static string GetPrivateSubnet(this NetworkConfig config) => config.PrivateSubnet;
    public static string GetPrivateSubnetMask(this NetworkConfig config) => config.PrivateSubnetMask;
}