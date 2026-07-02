using ALT_ICS.Shared.Models.Interfaces;

namespace ALT_ICS.Shared.Models;

/// <summary>
/// Manages the overall internet connection sharing lifecycle.
/// Coordinates NAT, DHCP, and DNS services.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Gets the current state of the sharing service.
    /// </summary>
    ServiceState State { get; }

    /// <summary>
    /// Gets the current network configuration.
    /// </summary>
    NetworkConfig Configuration { get; }

    /// <summary>
    /// Gets the NAT service instance.
    /// </summary>
    INATService Nat { get; }

    /// <summary>
    /// Initialises and starts all sharing sub-services.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully stops all sharing sub-services.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a new network configuration at runtime.
    /// </summary>
    Task ApplyConfigurationAsync(NetworkConfig config);

    /// <summary>
    /// Returns a combined health report for all sub-services.
    /// </summary>
    Task<HealthReport> GetHealthAsync();
}

/// <summary>
/// Describes the high-level service state.
/// </summary>
public enum ServiceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>
/// Health status of a single sub-service.
/// </summary>
public class ComponentHealth
{
    public string ComponentName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string? Message { get; set; }
    public TimeSpan Uptime { get; set; }
}

/// <summary>
/// Aggregated health report for the entire sharing service.
/// </summary>
public class HealthReport
{
    public bool OverallHealth { get; set; }
    public List<ComponentHealth> Components { get; set; } = new();
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
}
