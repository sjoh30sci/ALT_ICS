using ALT_ICS.Service.Logging;
using ALT_ICS.Service.Services;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service;

/// <summary>
/// <see cref="BackgroundService"/> that bridges the generic host with
/// the Windows Service lifecycle and delegates to <see cref="NetworkSharingService"/>.
/// </summary>
internal sealed class WindowsServiceHost : BackgroundService
{
    private readonly NetworkSharingService _sharing;
    private readonly ServiceEventLogger _eventLogger;
    private readonly ILogger<WindowsServiceHost> _logger;

    public WindowsServiceHost(
        NetworkSharingService sharing,
        ServiceEventLogger eventLogger,
        ILogger<WindowsServiceHost> logger)
    {
        _sharing = sharing;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _eventLogger.LogServiceStart();
        _logger.LogStateChange(nameof(WindowsServiceHost), "Starting", "Running");

        try
        {
            await _sharing.RunAsync(stoppingToken);
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested is false)
        {
            _eventLogger.LogCriticalFailure("WindowsServiceHost", ex);
            _logger.LogServiceError(nameof(WindowsServiceHost), ex, "Unhandled exception in service host");
            throw;
        }
        finally
        {
            _eventLogger.LogServiceStop();
            _logger.LogStateChange(nameof(WindowsServiceHost), "Running", "Stopped");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogStateChange(nameof(WindowsServiceHost), "Running", "Stopping");
        await base.StopAsync(cancellationToken);
    }
}
