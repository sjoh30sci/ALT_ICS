using Microsoft.Extensions.Logging;

namespace ALT_ICS.Shared.Utils;

/// <summary>
/// Shared logging helpers used across all ALT_ICS projects.
/// </summary>
public static class CommonLogger
{
    /// <summary>
    /// Event IDs used to categorise log messages.
    /// </summary>
    public static class EventIds
    {
        public static readonly EventId ServiceLifecycle  = new(1000, "ServiceLifecycle");
        public static readonly EventId NatEngine         = new(2000, "NatEngine");
        public static readonly EventId DhcpServer        = new(3000, "DhcpServer");
        public static readonly EventId DnsRelay          = new(4000, "DnsRelay");
        public static readonly EventId Configuration     = new(5000, "Configuration");
        public static readonly EventId HealthCheck       = new(6000, "HealthCheck");
        public static readonly EventId Error             = new(9999, "Error");
    }

    /// <summary>
    /// Logs a service lifecycle state change.
    /// </summary>
    public static void LogStateChange(this ILogger logger, string service, string fromState, string toState)
    {
        logger.LogInformation(EventIds.ServiceLifecycle,
            "Service [{Service}] transitioned from {FromState} → {ToState}",
            service, fromState, toState);
    }

    /// <summary>
    /// Logs an exception with a contextual message.
    /// </summary>
    public static void LogServiceError(this ILogger logger, string component, Exception ex, string? message = null)
    {
        logger.LogError(EventIds.Error, ex,
            "[{Component}] {Message}", component, message ?? ex.Message);
    }

    /// <summary>
    /// Logs a configuration change.
    /// </summary>
    public static void LogConfigChange(this ILogger logger, string property, string oldValue, string newValue)
    {
        logger.LogInformation(EventIds.Configuration,
            "Configuration changed: {Property} = \"{OldValue}\" → \"{NewValue}\"",
            property, oldValue, newValue);
    }
}
