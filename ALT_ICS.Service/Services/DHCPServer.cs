using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ALT_ICS.Service.Services;

/// <summary>
/// DHCP server that leases IPs on the private subnet.
/// Listens on UDP port 67, handles DISCOVER/OFFER/REQUEST/ACK per RFC 2131.
/// </summary>
public class DHCPServer : IDisposable
{
    private readonly ILogger<DHCPServer> _logger;
    private readonly NetworkConfig _config;
    private UdpClient? _socket;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private readonly ConcurrentDictionary<string, DhcpLease> _leases = new();
    private readonly HashSet<string> _availableIps = new();
    private readonly object _poolLock = new();
    private Timer? _cleanupTimer;

    public bool IsRunning => _isRunning;
    public IReadOnlyCollection<DhcpLease> Leases => _leases.Values.ToList().AsReadOnly();

    public DHCPServer(ILogger<DHCPServer> logger, NetworkConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        InitializeIpPool();

        _socket = new UdpClient(new IPEndPoint(IPAddress.Any, Constants.DhcpServerPort))
        {
            EnableBroadcast = true
        };

        _isRunning = true;
        _cleanupTimer = new Timer(_ => CleanupExpiredLeases(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _ = ListenForMessagesAsync(_cts.Token);

        _logger.LogInformation("DHCP server started on port {Port}, pool {Start}-{End}",
            Constants.DhcpServerPort, _config.DhcpPoolStart, _config.DhcpPoolEnd);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts?.Cancel();
        _cleanupTimer?.Dispose();
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
        }
        _leases.Clear();
        lock (_poolLock) _availableIps.Clear();
        _logger.LogInformation("DHCP server stopped");
        await Task.CompletedTask;
    }

    private void InitializeIpPool()
    {
        var start = IPAddress.Parse(_config.DhcpPoolStart);
        var end = IPAddress.Parse(_config.DhcpPoolEnd);
        var startBytes = start.GetAddressBytes();
        var endBytes = end.GetAddressBytes();
        var startVal = (startBytes[0] << 24) | (startBytes[1] << 16) | (startBytes[2] << 8) | startBytes[3];
        var endVal = (endBytes[0] << 24) | (endBytes[1] << 16) | (endBytes[2] << 8) | endBytes[3];

        lock (_poolLock)
        {
            _availableIps.Clear();
            for (var i = startVal; i <= endVal; i++)
            {
                var ip = new IPAddress(new byte[] { (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i });
                _availableIps.Add(ip.ToString());
            }
        }
        _logger.LogInformation("DHCP pool initialized with {Count} IPs", _availableIps.Count);
    }

    private async Task ListenForMessagesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _socket != null)
        {
            try
            {
                var result = await _socket.ReceiveAsync(ct);
                _ = ProcessDhcpMessageAsync(result.Buffer, result.RemoteEndPoint);
            }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogServiceError("DHCPServer", ex, "Error receiving DHCP message");
            }
        }
    }

    private async Task ProcessDhcpMessageAsync(byte[] data, IPEndPoint endpoint)
    {
        try
        {
            if (data.Length < 240) return;

            var op = data[0];
            if (op != 1) return; // Not a BOOTREQUEST

            var xid = new byte[4];
            Array.Copy(data, 4, xid, 0, 4);
            var clientMac = new byte[6];
            Array.Copy(data, 28, clientMac, 0, 6);
            var macStr = BitConverter.ToString(clientMac).Replace('-', ':');

            // Parse DHCP options
            var messageType = ParseDhcpMessageType(data);
            if (messageType == null) return;

            var gatewayIp = IPAddress.Parse(_config.GatewayIp);
            var subnetMask = IPAddress.Parse(_config.PrivateSubnetMask);

            switch (messageType)
            {
                case 1: // DISCOVER
                    await HandleDiscover(xid, clientMac, macStr, gatewayIp, subnetMask);
                    break;
                case 3: // REQUEST
                    var requestedIp = ParseRequestedIp(data) ?? endpoint.Address;
                    await HandleRequest(xid, clientMac, macStr, requestedIp, gatewayIp, subnetMask);
                    break;
                case 4: // DECLINE
                    var declinedIp = ParseRequestedIp(data);
                    if (declinedIp != null)
                    {
                        lock (_poolLock) _availableIps.Add(declinedIp.ToString());
                        _leases.TryRemove(macStr, out _);
                    }
                    _logger.LogWarning("DHCP DECLINE from {Mac} for {Ip}", macStr, declinedIp);
                    break;
                case 7: // RELEASE
                    _leases.TryRemove(macStr, out var released);
                    if (released != null)
                    {
                        lock (_poolLock) _availableIps.Add(released.IpAddress);
                    }
                    _logger.LogInformation("DHCP RELEASE from {Mac}", macStr);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogServiceError("DHCPServer", ex, "Error processing DHCP message");
        }
        await Task.CompletedTask;
    }

    private async Task HandleDiscover(byte[] xid, byte[] clientMac, string macStr, IPAddress gatewayIp, IPAddress subnetMask)
    {
        if (_leases.ContainsKey(macStr))
        {
            // Extend existing lease
            if (_leases.TryGetValue(macStr, out var existing))
            {
                await SendDhcpResponse(2, xid, clientMac, IPAddress.Parse(existing.IpAddress), IPAddress.Any, gatewayIp, subnetMask);
            }
            return;
        }

        string? offeredIp;
        lock (_poolLock)
        {
            if (_availableIps.Count == 0) return;
            offeredIp = _availableIps.First();
            _availableIps.Remove(offeredIp);
        }

        if (offeredIp != null)
        {
            await SendDhcpResponse(2, xid, clientMac, IPAddress.Parse(offeredIp), IPAddress.Any, gatewayIp, subnetMask);
        }
    }

    private async Task HandleRequest(byte[] xid, byte[] clientMac, string macStr, IPAddress requestedIp, IPAddress gatewayIp, IPAddress subnetMask)
    {
        var ipStr = requestedIp.ToString();
        bool canAssign;

        lock (_poolLock)
        {
            canAssign = _availableIps.Contains(ipStr) || _leases.ContainsKey(macStr);
            if (canAssign && !_leases.ContainsKey(macStr))
            {
                _availableIps.Remove(ipStr);
            }
        }

        if (canAssign)
        {
            var lease = new DhcpLease
            {
                IpAddress = ipStr,
                MacAddress = macStr,
                Hostname = "",
                LeasedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_config.DhcpLeaseTimeMinutes)
            };
            _leases[macStr] = lease;

            await SendDhcpResponse(5, xid, clientMac, requestedIp, requestedIp, gatewayIp, subnetMask);
            _logger.LogInformation("DHCP ACK: {Ip} → {Mac}", ipStr, macStr);
        }
        else
        {
            await SendDhcpNak(xid, clientMac, gatewayIp);
            _logger.LogWarning("DHCP NAK: {Ip} not available for {Mac}", ipStr, macStr);
        }
    }

    private async Task SendDhcpResponse(byte messageType, byte[] xid, byte[] clientMac, IPAddress yiaddr, IPAddress ciaddr, IPAddress gatewayIp, IPAddress subnetMask)
    {
        if (_socket == null) return;
        var packet = BuildDhcpPacket(messageType, xid, clientMac, yiaddr, ciaddr, gatewayIp, subnetMask);
        var destIp = messageType == 2 ? IPAddress.Broadcast : yiaddr;
        await _socket.SendAsync(packet, packet.Length, new IPEndPoint(destIp, Constants.DhcpClientPort));
    }

    private async Task SendDhcpNak(byte[] xid, byte[] clientMac, IPAddress gatewayIp)
    {
        if (_socket == null) return;
        var packet = BuildDhcpPacket(6, xid, clientMac, IPAddress.Any, IPAddress.Any, gatewayIp, IPAddress.Parse(_config.PrivateSubnetMask));
        await _socket.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, Constants.DhcpClientPort));
    }

    private byte[] BuildDhcpPacket(byte messageType, byte[] xid, byte[] clientMac, IPAddress yiaddr, IPAddress ciaddr, IPAddress gatewayIp, IPAddress subnetMask)
    {
        var packet = new byte[300];
        packet[0] = 2; // BOOTREPLY
        packet[1] = 1; // Ethernet
        packet[2] = 6; // MAC length
        packet[3] = 0; // Hops
        Array.Copy(xid, 0, packet, 4, 4);
        packet[8] = 0; packet[9] = 0; // Secs
        packet[10] = 0x80; packet[11] = 0x00; // Flags = Broadcast

        // ciaddr
        var ciBytes = ciaddr.GetAddressBytes();
        Array.Copy(ciBytes, 0, packet, 12, 4);
        // yiaddr
        var yiBytes = yiaddr.GetAddressBytes();
        Array.Copy(yiBytes, 0, packet, 16, 4);
        // siaddr (gateway)
        var giBytes = gatewayIp.GetAddressBytes();
        Array.Copy(giBytes, 0, packet, 20, 4);
        // giaddr
        Array.Copy(giBytes, 0, packet, 24, 4);
        // chaddr
        Array.Copy(clientMac, 0, packet, 28, 6);

        // Magic cookie
        packet[236] = 99;
        packet[237] = 130;
        packet[238] = 83;
        packet[239] = 99;

        var idx = 240;

        // Option 53: DHCP Message Type
        packet[idx++] = 53;
        packet[idx++] = 1;
        packet[idx++] = messageType;

        // Option 1: Subnet Mask
        packet[idx++] = 1;
        packet[idx++] = 4;
        var maskBytes = subnetMask.GetAddressBytes();
        Array.Copy(maskBytes, 0, packet, idx, 4);
        idx += 4;

        // Option 3: Router
        packet[idx++] = 3;
        packet[idx++] = 4;
        Array.Copy(giBytes, 0, packet, idx, 4);
        idx += 4;

        // Option 6: DNS Server
        packet[idx++] = 6;
        packet[idx++] = 8;
        var dns1 = IPAddress.Parse(_config.PrimaryDns).GetAddressBytes();
        var dns2 = IPAddress.Parse(_config.SecondaryDns).GetAddressBytes();
        Array.Copy(dns1, 0, packet, idx, 4);
        Array.Copy(dns2, 0, packet, idx + 4, 4);
        idx += 8;

        // Option 51: Lease Time
        packet[idx++] = 51;
        packet[idx++] = 4;
        var leaseTime = BitConverter.GetBytes(_config.DhcpLeaseTimeMinutes * 60);
        if (BitConverter.IsLittleEndian) Array.Reverse(leaseTime);
        Array.Copy(leaseTime, 0, packet, idx, 4);
        idx += 4;

        // Option 54: Server Identifier
        packet[idx++] = 54;
        packet[idx++] = 4;
        Array.Copy(giBytes, 0, packet, idx, 4);
        idx += 4;

        // End option
        packet[idx++] = 255;

        Array.Resize(ref packet, idx);
        return packet;
    }

    private byte? ParseDhcpMessageType(byte[] data)
    {
        if (data.Length < 240) return null;
        for (int i = 240; i < data.Length - 2; i++)
        {
            if (data[i] == 53 && data[i + 1] == 1)
                return data[i + 2];
        }
        return null;
    }

    private IPAddress? ParseRequestedIp(byte[] data)
    {
        if (data.Length < 240) return null;
        for (int i = 240; i < data.Length - 6; i++)
        {
            if (data[i] == 50 && data[i + 1] == 4)
                return new IPAddress(new byte[] { data[i + 2], data[i + 3], data[i + 4], data[i + 5] });
        }
        return null;
    }

    private void CleanupExpiredLeases()
    {
        var now = DateTime.UtcNow;
        var expired = _leases.Where(kvp => kvp.Value.ExpiresAt <= now).ToList();
        foreach (var kvp in expired)
        {
            if (_leases.TryRemove(kvp.Key, out var lease))
            {
                lock (_poolLock) _availableIps.Add(lease.IpAddress);
                _logger.LogInformation("DHCP lease expired: {Ip} for {Mac}", lease.IpAddress, lease.MacAddress);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cts?.Cancel();
        _socket?.Close();
        _cts?.Dispose();
    }
}

public class DhcpLease
{
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public DateTime LeasedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
