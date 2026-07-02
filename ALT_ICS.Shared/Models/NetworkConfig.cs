namespace ALT_ICS.Shared.Models;

/// <summary>
/// Configuration model for the internet connection sharing service.
/// Maps to the "NetworkConfig" section in appsettings.json.
/// </summary>
public class NetworkConfig
{
    /// <summary>The name of the public / upstream network interface.</summary>
    public string PublicInterface { get; set; } = "Wi-Fi";

    /// <summary>The name of the private / downstream network interface.</summary>
    public string PrivateInterface { get; set; } = "Ethernet";

    /// <summary>Subnet for the private network (e.g. "192.168.137.0").</summary>
    public string PrivateSubnet { get; set; } = "192.168.137.0";

    /// <summary>Subnet mask for the private network.</summary>
    public string PrivateSubnetMask { get; set; } = "255.255.255.0";

    /// <summary>Gateway IP assigned to the private interface.</summary>
    public string GatewayIp { get; set; } = "192.168.137.1";

    /// <summary>DHCP pool start address.</summary>
    public string DhcpPoolStart { get; set; } = "192.168.137.100";

    /// <summary>DHCP pool end address.</summary>
    public string DhcpPoolEnd { get; set; } = "192.168.137.200";

    /// <summary>DHCP lease duration in minutes.</summary>
    public int DhcpLeaseTimeMinutes { get; set; } = 1440; // 24 hours

    /// <summary>Primary DNS server to relay to (upstream).</summary>
    public string PrimaryDns { get; set; } = "8.8.8.8";

    /// <summary>Secondary DNS server to relay to (upstream).</summary>
    public string SecondaryDns { get; set; } = "8.8.4.4";

    /// <summary>Whether to auto-start sharing when the service boots.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Interval in seconds between health checks.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>Whether to enable verbose logging.</summary>
    public bool VerboseLogging { get; set; } = false;
}
