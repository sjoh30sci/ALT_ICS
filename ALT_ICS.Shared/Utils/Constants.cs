namespace ALT_ICS.Shared.Utils;

/// <summary>
/// Application-wide constants used by all ALT_ICS projects.
/// </summary>
public static class Constants
{
    /// <summary>Display name shown in service manager and UI.</summary>
    public const string AppName = "ALT_ICS";
    /// <summary>Product description.</summary>
    public const string AppDescription = "Alternative Internet Connection Sharing";

    /// <summary>Service name used for Windows Service registration.</summary>
    public const string ServiceName = "ALT_ICS";
    /// <summary>Service display name in the Services MMC snap-in.</summary>
    public const string ServiceDisplayName = "ALT_ICS — Alternative Internet Connection Sharing";

    /// <summary>Default TCP port for the SignalR hub.</summary>
    public const int SignalRPort = 51000;
    /// <summary>Default HTTP port for the health / info endpoint.</summary>
    public const int HealthHttpPort = 51001;

    /// <summary>SignalR hub endpoint path.</summary>
    public const string SignalRHubPath = "/hubs/monitor";

    /// <summary>DHCP server UDP port.</summary>
    public const int DhcpServerPort = 67;
    /// <summary>DHCP client UDP port.</summary>
    public const int DhcpClientPort = 68;

    /// <summary>DNS server UDP port.</summary>
    public const int DnsPort = 53;

    /// <summary>Maximum size of a UDP packet the NAT will handle.</summary>
    public const int MaxUdpPacketSize = 65535;

    /// <summary>Default TTL for NAT sessions (seconds).</summary>
    public const int DefaultNatSessionTimeoutSeconds = 300;

    /// <summary>Interval at which the health monitor runs (milliseconds).</summary>
    public const int HealthMonitorIntervalMs = 30_000;

    /// <summary>Event log source name.</summary>
    public const string EventLogSource = "ALT_ICS";
    /// <summary>Event log name.</summary>
    public const string EventLogName = "ALT_ICS";
}
