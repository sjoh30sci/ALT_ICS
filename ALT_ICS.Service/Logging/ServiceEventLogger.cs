using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service.Logging;

/// <summary>
/// Wraps the Windows Event Log for structured service-level logging.
/// </summary>
public class ServiceEventLogger
{
    private readonly ILogger<ServiceEventLogger> _logger;

    /// <summary>
    /// The event ID used for service lifecycle events.
    /// </summary>
    public static readonly EventId ServiceEvent = new(100, nameof(ServiceEventLogger));

    public ServiceEventLogger(ILogger<ServiceEventLogger> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs a service start event to the Windows Event Log.
    /// </summary>
    public void LogServiceStart()
    {
        _logger.LogInformation(ServiceEvent, "ALT_ICS service started (version {Version})",
            GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
    }

    /// <summary>
    /// Logs a service stop event to the Windows Event Log.
    /// </summary>
    public void LogServiceStop()
    {
        _logger.LogInformation(ServiceEvent, "ALT_ICS service stopped");
    }

    /// <summary>
    /// Logs a critical failure that requires administrative attention.
    /// </summary>
    public void LogCriticalFailure(string component, Exception ex)
    {
        _logger.LogCritical(ServiceEvent, ex,
            "Critical failure in component [{Component}]", component);
    }

    /// <summary>
    /// Logs a configuration-related event.
    /// </summary>
    public void LogConfigurationEvent(string message)
    {
        _logger.LogInformation(CommonLogger.EventIds.Configuration, message);
    }

    /// <summary>
    /// Writes an audit entry (success/failure) to the event log.
    /// </summary>
    public void LogAudit(string action, bool success, string? detail = null)
    {
        if (success)
            _logger.LogInformation("Audit [OK]  {Action} — {Detail}", action, detail ?? "(no detail)");
        else
            _logger.LogWarning("Audit [FAIL] {Action} — {Detail}", action, detail ?? "(no detail)");
    }
}
