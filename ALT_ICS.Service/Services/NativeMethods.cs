using System;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Net;

namespace ALT_ICS.Service.Services;

/// <summary>
/// Windows API P/Invoke declarations for raw socket and network operations.
/// Required for raw socket support on Windows (requires Administrator privileges).
/// </summary>
internal static class NativeMethods
{
    // ============================================================
    // Socket Options (IP_HDRINCL, etc.)
    // ============================================================

    /// <summary>
    /// IP_HDRINCL socket option: include IP header with data.
    /// Required for raw sockets on Windows.
    /// </summary>
    public const int IP_HDRINCL = 2;

    /// <summary>
    /// IPPROTO_IP level for setsockopt/getsockopt.
    /// </summary>
    public const int IPPROTO_IP = 0;

    /// <summary>
    /// IPPROTO_ICMP protocol constant.
    /// </summary>
    public const int IPPROTO_ICMP = 1;

    /// <summary>
    /// IPPROTO_TCP protocol constant.
    /// </summary>
    public const int IPPROTO_TCP = 6;

    /// <summary>
    /// IPPROTO_UDP protocol constant.
    /// </summary>
    public const int IPPROTO_UDP = 17;

    /// <summary>
    /// IPPROTO_RAW protocol constant (raw IP packets).
    /// </summary>
    public const int IPPROTO_RAW = 255;

    /// <summary>
    /// SIO_RCVALL ioctl code: enable promiscuous mode / receive all packets.
    /// </summary>
    public const int SIO_RCVALL = unchecked((int)0x98000001);

    /// <summary>
    /// RCVALL_ON value for SIO_RCVALL.
    /// </summary>
    public const int RCVALL_ON = 1;

    /// <summary>
    /// RCVALL_OFF value for SIO_RCVALL.
    /// </summary>
    public const int RCVALL_OFF = 0;

    // ============================================================
    // Windows Sockets API (Winsock) - ws2_32.dll
    // ============================================================

    /// <summary>
    /// Creates a socket.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern SafeSocketHandle WSASocketW(
        AddressFamily addressFamily,
        SocketType socketType,
        ProtocolType protocolType,
        IntPtr protocolInfo,
        uint group,
        SocketFlags flags);

    /// <summary>
    /// Sets a socket option.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int setsockopt(
        SafeSocketHandle socketHandle,
        int level,
        int optionName,
        ref int optionValue,
        int optionLen);

    /// <summary>
    /// Gets a socket option.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int getsockopt(
        SafeSocketHandle socketHandle,
        int level,
        int optionName,
        out int optionValue,
        ref int optionLen);

    /// <summary>
    /// Controls socket I/O modes (ioctlsocket).
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int ioctlsocket(
        SafeSocketHandle socketHandle,
        int cmd,
        ref int argp);

    /// <summary>
    /// Binds a socket to a local address.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int bind(
        SafeSocketHandle socketHandle,
        ref sockaddr_in name,
        int namelen);

    /// <summary>
    /// Receives data from a socket.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int recvfrom(
        SafeSocketHandle socketHandle,
        IntPtr buf,
        int len,
        SocketFlags flags,
        ref sockaddr_in from,
        ref int fromlen);

    /// <summary>
    /// Sends data to a specific destination.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int sendto(
        SafeSocketHandle socketHandle,
        IntPtr buf,
        int len,
        SocketFlags flags,
        ref sockaddr_in to,
        int tolen);

    /// <summary>
    /// Closes a socket.
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int closesocket(SafeSocketHandle socketHandle);

    /// <summary>
    /// Converts an IP address string to binary form (inet_pton equivalent).
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern int inet_pton(
        AddressFamily af,
        [MarshalAs(UnmanagedType.LPStr)] string src,
        out in_addr dst);

    /// <summary>
    /// Converts binary IP address to string (inet_ntop equivalent).
    /// </summary>
    [DllImport("ws2_32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr inet_ntop(
        AddressFamily af,
        ref in_addr src,
        IntPtr dst,
        int size);

    // ============================================================
    // IP Helper API (iphlpapi.dll) - for interface enumeration, IP forwarding
    // ============================================================

    /// <summary>
    /// Retrieves the IP interface table (IPv4).
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetIpAddrTable(
        IntPtr pIpAddrTable,
        ref uint pdwSize,
        bool bOrder);

    /// <summary>
    /// Retrieves the adapter addresses (IPv4/IPv6).
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetAdaptersAddresses(
        uint Family,
        uint Flags,
        IntPtr Reserved,
        IntPtr pAdapterAddresses,
        ref uint pOutBufLen);

    /// <summary>
    /// Enables or disables IP forwarding (routing) on the system.
    /// Requires Administrator privileges and reboot to persist.
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint SetIpForwardEntry(IntPtr pRoute);

    /// <summary>
    /// Retrieves the best route to a destination IP.
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetBestRoute(
        uint dwDestAddr,
        uint dwSourceAddr,
        out MIB_IPFORWARDROW pBestRoute);

    /// <summary>
    /// Creates a new route entry (for adding static routes if needed).
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint CreateIpForwardEntry(IntPtr pRoute);

    /// <summary>
    /// Deletes a route entry.
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint DeleteIpForwardEntry(IntPtr pRoute);

    // ============================================================
    // Structures
    // ============================================================

    /// <summary>
    /// IPv4 socket address structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct sockaddr_in
    {
        public short sin_family;
        public ushort sin_port;
        public in_addr sin_addr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] sin_zero;

        public sockaddr_in()
        {
            sin_zero = new byte[8];
        }
    }

    /// <summary>
    /// IPv4 address structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct in_addr
    {
        public uint S_addr;

        public override string ToString()
        {
            byte[] bytes = BitConverter.GetBytes(S_addr);
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
        }
    }

    /// <summary>
    /// IP address table entry (IPv4).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPADDRROW
    {
        public uint dwAddr;
        public uint dwIndex;
        public uint dwMask;
        public uint dwBCastAddr;
        public uint dwReasmSize;
        public ushort unused1;
        public ushort unused2;
    }

    /// <summary>
    /// IP address table (IPv4).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPADDRTABLE
    {
        public uint dwNumEntries;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public MIB_IPADDRROW[] table;
    }

    /// <summary>
    /// IP forward table row (route entry).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MIB_IPFORWARDROW
    {
        public uint dwForwardDest;
        public uint dwForwardMask;
        public uint dwForwardPolicy;
        public uint dwForwardNextHop;
        public uint dwForwardIfIndex;
        public uint dwForwardType;
        public uint dwForwardProto;
        public uint dwForwardAge;
        public uint dwForwardNextHopAS;
        public uint dwForwardMetric1;
        public uint dwForwardMetric2;
        public uint dwForwardMetric3;
        public uint dwForwardMetric4;
        public uint dwForwardMetric5;
    }

    /// <summary>
    /// Adapter addresses structure (simplified).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct IP_ADAPTER_ADDRESSES
    {
        public uint Length;
        public uint IfIndex;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? AdapterName;
        public IntPtr FirstUnicastAddress;
        public IntPtr FirstAnycastAddress;
        public IntPtr FirstMulticastAddress;
        public IntPtr FirstDnsServerAddress;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? DnsSuffix;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Description;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? FriendlyName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] PhysicalAddress;
        public uint PhysicalAddressLength;
        public uint Flags;
        public uint Mtu;
        public uint IfType;
        public OperStatus OperStatus;
        public uint Ipv6IfIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public ulong[] ZoneIndices;
        public IntPtr FirstPrefix;
        public IntPtr Next;
    }

    /// <summary>
    /// Operational status of a network interface.
    /// </summary>
    public enum OperStatus : uint
    {
        IfOperStatusUp = 1,
        IfOperStatusDown = 2,
        IfOperStatusTesting = 3,
        IfOperStatusUnknown = 4,
        IfOperStatusDormant = 5,
        IfOperStatusNotPresent = 6,
        IfOperStatusLowerLayerDown = 7,
    }

    // ============================================================
    // Safe Handles for proper resource cleanup
    // ============================================================

    /// <summary>
    /// Safe handle for socket handles from WSASocket.
    /// </summary>
    public sealed class SafeSocketHandle : SafeHandle
    {
        public SafeSocketHandle() : base(IntPtr.Zero, true) { }
        public SafeSocketHandle(IntPtr handle) : base(handle, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                closesocket(this);
                handle = IntPtr.Zero;
                return true;
            }
            return false;
        }
    }
}