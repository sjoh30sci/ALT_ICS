namespace ALT_ICS.Shared.Models.Interfaces;

/// <summary>
/// Defines the NAT (Network Address Translation) service contract.
/// </summary>
public interface INATService
{
    /// <summary>
    /// Gets whether the NAT service is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the name of the public (external) network interface.
    /// </summary>
    string PublicInterface { get; }

    /// <summary>
    /// Gets the name of the private (internal) network interface.
    /// </summary>
    string PrivateInterface { get; }

    /// <summary>
    /// Starts the NAT translation engine.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the NAT translation engine.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns current NAT mapping statistics.
    /// </summary>
    /// <returns>A snapshot of active session counts and throughput.</returns>
    Task<NATStats> GetStatsAsync();

    /// <summary>
    /// Updates the interfaces used for NAT.
    /// </summary>
    Task ConfigureInterfacesAsync(string publicInterface, string privateInterface);
}

/// <summary>
/// Snapshot of NAT performance counters.
/// </summary>
public class NATStats
{
    /// <summary>Number of active NAT sessions.</summary>
    public int ActiveSessions { get; set; }
    /// <summary>Total bytes forwarded since start.</summary>
    public long TotalBytesForwarded { get; set; }
    /// <summary>Current throughput in bytes per second.</summary>
    public long ThroughputBps { get; set; }
    /// <summary>Total packets translated.</summary>
    public long PacketsTranslated { get; set; }
    /// <summary>Total packets dropped due to errors.</summary>
    public long PacketsDropped { get; set; }
    /// <summary>Timestamp of this snapshot.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a single NAT translation entry (session).
/// </summary>
public interface ITranslationEntry
{
    /// <summary>Unique session identifier (e.g., "srcIP:srcPort-dstIP:dstPort").</summary>
    string SessionId { get; set; }

    /// <summary>Original source IP address (internal client).</summary>
    string OriginalSourceIp { get; set; }

    /// <summary>Original source port (internal client).</summary>
    int OriginalSourcePort { get; set; }

    /// <summary>Translated source IP address (public interface IP).</summary>
    string TranslatedSourceIp { get; set; }

    /// <summary>Translated source port (mapped public port).</summary>
    int TranslatedSourcePort { get; set; }

    /// <summary>Destination IP address.</summary>
    string DestinationIp { get; set; }

    /// <summary>Destination port.</summary>
    int DestinationPort { get; set; }

    /// <summary>Protocol ("TCP" or "UDP").</summary>
    string Protocol { get; set; }

    /// <summary>Session creation timestamp (UTC).</summary>
    DateTime CreatedAt { get; set; }

    /// <summary>Last activity timestamp (UTC).</summary>
    DateTime LastActivity { get; set; }

    /// <summary>Total bytes transmitted in this session.</summary>
    long BytesTransferred { get; set; }

    /// <summary>Total packets transmitted in this session.</summary>
    long PacketsTransmitted { get; set; }

    /// <summary>Indicates if this session is for inbound (inbound mapping) or outbound traffic.</summary>
    bool IsInbound { get; set; }

    /// <summary>Checks if the session has expired based on the given timeout.</summary>
    bool IsExpired(TimeSpan timeout);
}

/// <summary>
/// Thread-safe session table for tracking active NAT sessions.
/// </summary>
public interface ISessionTable
{
    /// <summary>Gets the number of active sessions.</summary>
    int Count { get; }

    /// <summary>Finds an existing session or creates a new one for outbound traffic.</summary>
    ITranslationEntry? FindOrCreateOutboundSession(string sourceIp, int sourcePort, string destinationIp, int destinationPort, string protocol, string translatedIp, int translatedPort);

    /// <summary>Finds an existing session for inbound traffic (reverse lookup).</summary>
    ITranslationEntry? FindInboundSession(string destinationIp, int destinationPort, string sourceIp, int sourcePort, string protocol);

    /// <summary>Removes a session by its ID.</summary>
    bool RemoveSession(string sessionId);

    /// <summary>Removes all expired sessions based on the given timeout.</summary>
    int RemoveExpiredSessions(TimeSpan timeout);

    /// <summary>Gets all active sessions.</summary>
    IReadOnlyCollection<ITranslationEntry> GetAllSessions();

    /// <summary>Updates the last activity timestamp for a session.</summary>
    void UpdateActivity(string sessionId, long bytesTransferred = 0);

    /// <summary>Clears all sessions.</summary>
    void Clear();
}
