using ALT_ICS.Service.Services;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service.Hubs;

/// <summary>
/// SignalR hub for real-time communication between the Windows Service and the GUI.
/// </summary>
public class MonitorHub : Hub
{
    private readonly NetworkSharingService _sharing;
    private readonly NATConnectionService _nat;
    private readonly ILogger<MonitorHub> _logger;

    public MonitorHub(
        NetworkSharingService sharing,
        NATConnectionService nat,
        ILogger<MonitorHub> logger)
    {
        _sharing = sharing;
        _nat = nat;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("GUI client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("GUI client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HealthReport> StartSharing()
    {
        _logger.LogInformation("StartSharing requested by GUI");
        try
        {
            await _sharing.StartAsync();
            return await _sharing.GetHealthAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start sharing");
            return new HealthReport
            {
                OverallHealth = false,
                Components = new List<ComponentHealth>
                {
                    new() { ComponentName = "NAT", IsHealthy = false, Message = ex.Message },
                    new() { ComponentName = "DHCP", IsHealthy = false, Message = ex.Message },
                    new() { ComponentName = "DNS", IsHealthy = false, Message = ex.Message }
                },
                ReportedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<HealthReport> StopSharing()
    {
        _logger.LogInformation("StopSharing requested by GUI");
        try
        {
            await _sharing.StopAsync();
            _logger.LogInformation("StopSharing completed successfully");
            return new HealthReport
            {
                OverallHealth = false,
                Components = new List<ComponentHealth>
                {
                    new() { ComponentName = "NAT", IsHealthy = false, Message = "Stopped" },
                    new() { ComponentName = "DHCP", IsHealthy = false, Message = "Stopped" },
                    new() { ComponentName = "DNS", IsHealthy = false, Message = "Stopped" }
                },
                ReportedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop sharing");
            // Return a stopped report anyway
            return new HealthReport
            {
                OverallHealth = false,
                Components = new List<ComponentHealth>
                {
                    new() { ComponentName = "NAT", IsHealthy = false, Message = "Stopped" },
                    new() { ComponentName = "DHCP", IsHealthy = false, Message = "Stopped" },
                    new() { ComponentName = "DNS", IsHealthy = false, Message = "Stopped" }
                },
                ReportedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<HealthReport> GetHealth()
    {
        return await _sharing.GetHealthAsync();
    }

    public async Task<NATStats> GetNatStats()
    {
        return await _nat.GetStatsAsync();
    }
}
