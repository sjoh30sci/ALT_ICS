using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service.Services;

/// <summary>
/// Coordinates the lifecycle of NAT, DHCP, and DNS services.
/// Implements <see cref="IConnectionManager"/>.
/// </summary>
public class NetworkSharingService : IConnectionManager
{
    private readonly NATConnectionService _nat;
    private readonly DHCPServer _dhcp;
    private readonly DNSRelayService _dns;
    private readonly NetworkConfig _config;
    private readonly ILogger<NetworkSharingService> _logger;
    private readonly CancellationTokenSource _internalCts = new();

    private ServiceState _state = ServiceState.Stopped;
    private DateTime _startedAt;

    public ServiceState State => _state;
    public NetworkConfig Configuration => _config;
    public INATService Nat => _nat;

    public NetworkSharingService(
        NATConnectionService nat,
        DHCPServer dhcp,
        DNSRelayService dns,
        NetworkConfig config,
        ILogger<NetworkSharingService> logger)
    {
        _nat = nat;
        _dhcp = dhcp;
        _dns = dns;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ServiceState.Stopped)
            return;

        _state = ServiceState.Starting;
        _logger.LogStateChange(nameof(NetworkSharingService), "Stopped", "Starting");

        try
        {
            await _nat.ConfigureInterfacesAsync(_config.PublicInterface, _config.PrivateInterface);
            await _nat.StartAsync(cancellationToken);

            await _dhcp.StartAsync(cancellationToken);

            await _dns.StartAsync(cancellationToken);

            _state = ServiceState.Running;
            _startedAt = DateTime.UtcNow;
            _logger.LogStateChange(nameof(NetworkSharingService), "Starting", "Running");
        }
        catch (Exception ex)
        {
            _state = ServiceState.Faulted;
            _logger.LogServiceError(nameof(NetworkSharingService), ex, "Failed to start sharing services");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state == ServiceState.Stopped)
            return;

        _state = ServiceState.Stopping;
        _logger.LogStateChange(nameof(NetworkSharingService), "Running", "Stopping");

        try
        {
            await _dns.StopAsync(cancellationToken);
            await _dhcp.StopAsync(cancellationToken);
            await _nat.StopAsync(cancellationToken);
        }
        finally
        {
            _state = ServiceState.Stopped;
            _logger.LogStateChange(nameof(NetworkSharingService), "Stopping", "Stopped");
        }
    }

    /// <inheritdoc />
    public async Task ApplyConfigurationAsync(NetworkConfig config)
    {
        if (_state == ServiceState.Running)
        {
            await _nat.ConfigureInterfacesAsync(config.PublicInterface, config.PrivateInterface);
        }
        
        _logger.LogConfigChange("FullConfig", _config.PublicInterface, config.PublicInterface);
        
        // Update config properties
        _config.PublicInterface = config.PublicInterface;
        _config.PrivateInterface = config.PrivateInterface;
        _config.DhcpPoolStart = config.DhcpPoolStart;
        _config.DhcpPoolEnd = config.DhcpPoolEnd;
        _config.PrimaryDns = config.PrimaryDns;
        _config.SecondaryDns = config.SecondaryDns;
        _config.DhcpLeaseTimeMinutes = config.DhcpLeaseTimeMinutes;
        _config.AutoStart = config.AutoStart;
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<HealthReport> GetHealthAsync()
    {
        var components = new List<ComponentHealth>
        {
            new()
            {
                ComponentName = "NAT",
                IsHealthy = _nat.IsRunning,
                Uptime = _nat.IsRunning ? DateTime.UtcNow - _startedAt : TimeSpan.Zero
            },
            new()
            {
                ComponentName = "DHCP",
                IsHealthy = _dhcp.IsRunning,
                Uptime = _dhcp.IsRunning ? DateTime.UtcNow - _startedAt : TimeSpan.Zero
            },
            new()
            {
                ComponentName = "DNS",
                IsHealthy = _dns.IsRunning,
                Uptime = _dns.IsRunning ? DateTime.UtcNow - _startedAt : TimeSpan.Zero
            }
        };

        return await Task.FromResult(new HealthReport
        {
            OverallHealth = components.TrueForAll(c => c.IsHealthy),
            Components = components,
            ReportedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Background worker entry point called by <see cref="WindowsServiceHost"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_config.AutoStart)
            await StartAsync(cancellationToken);

        try
        {
            // Keep the service alive; perform periodic health checks.
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_config.HealthCheckIntervalSeconds * 1000, cancellationToken);
                var health = await GetHealthAsync();
                if (!health.OverallHealth)
                    _logger.LogWarning("Health check degraded: {Components}",
                        string.Join(", ", health.Components.Where(c => !c.IsHealthy).Select(c => c.ComponentName)));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        finally
        {
            await StopAsync(CancellationToken.None);
        }
    }
}
