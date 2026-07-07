using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.GUI.Services;

/// <summary>
/// SignalR client that connects to the ALT_ICS Service hub.
/// Currently a stub — will be wired to the real hub once the service
/// exposes a SignalR endpoint.
/// </summary>
public class ServiceClient : IAsyncDisposable
{
    private readonly ILogger<ServiceClient> _logger;
    private HubConnection? _connection;

    /// <summary>
    /// Fired when updated NAT stats arrive from the service.
    /// </summary>
    public event Action<NATStats>? OnNatStatsUpdated;

    /// <summary>
    /// Fired when a health report arrives from the service.
    /// </summary>
    public event Action<HealthReport>? OnHealthReportUpdated;

    /// <summary>
    /// Fired when the connection state changes.
    /// </summary>
    public event Action<bool>? OnConnectionStateChanged;

    /// <summary>Whether the client is currently connected to the service hub.</summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>The underlying hub connection (exposed for advanced use).</summary>
    public HubConnection? Connection => _connection;

    public ServiceClient(ILogger<ServiceClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds and starts the SignalR connection to the service.
    /// </summary>
    public async Task ConnectAsync(string? url = null)
    {
        url ??= $"http://localhost:{Constants.SignalRPort}";

        _connection = new HubConnectionBuilder()
            .WithUrl($"{url}{Constants.SignalRHubPath}")
            .WithAutomaticReconnect(new RetryPolicy())
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .Build();

        // Register event handlers from the hub.
        _connection.On<NATStats>("NatStatsUpdated", stats =>
        {
            OnNatStatsUpdated?.Invoke(stats);
        });

        _connection.On<HealthReport>("HealthReportUpdated", report =>
        {
            OnHealthReportUpdated?.Invoke(report);
        });

        _connection.Closed += async (error) =>
        {
            _logger.LogWarning("SignalR connection closed: {Message}", error?.Message);
            OnConnectionStateChanged?.Invoke(false);

            // Automatic reconnect is configured, but we can add fallback logic here.
            await Task.CompletedTask;
        };

        _connection.Reconnected += async (connectionId) =>
        {
            _logger.LogInformation("SignalR reconnected (id={Id})", connectionId);
            OnConnectionStateChanged?.Invoke(true);
            await Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync();
            _logger.LogInformation("Connected to ALT_ICS service at {Url}", url);
            OnConnectionStateChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.LogServiceError(nameof(ServiceClient), ex, "Failed to connect to service");
            throw;
        }
    }

    /// <summary>
    /// Sends a start-sharing request to the service hub.
    /// </summary>
    public async Task<HealthReport> StartSharingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<HealthReport>("StartSharing", ct);
    }

    /// <summary>
    /// Sends a stop-sharing request to the service hub.
    /// </summary>
    public async Task<HealthReport> StopSharingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<HealthReport>("StopSharing", ct);
    }

    /// <summary>
    /// Requests the latest health report from the service.
    /// </summary>
    public async Task<HealthReport?> RequestHealthAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<HealthReport>("GetHealth", ct);
    }

    /// <summary>
    /// Requests the latest NAT stats from the service.
    /// </summary>
    public async Task<NATStats?> RequestNatStatsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<NATStats>("GetNatStats", ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to the ALT_ICS service. Call ConnectAsync first.");
    }

    /// <summary>
    /// Exponential-backoff retry policy for automatic reconnection.
    /// </summary>
    private sealed class RetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount >= 5)
                return null; // Give up after 5 attempts.

            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryContext.PreviousRetryCount));
            return delay;
        }
    }
}
