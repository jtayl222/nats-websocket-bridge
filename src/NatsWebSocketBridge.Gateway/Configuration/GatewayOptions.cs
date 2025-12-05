namespace NatsWebSocketBridge.Gateway.Configuration;

/// <summary>
/// Configuration for the gateway
/// </summary>
public class GatewayOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Gateway";
    
    /// <summary>
    /// Maximum message size in bytes
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024; // 1MB default
    
    /// <summary>
    /// Message rate limit per device (messages per second)
    /// </summary>
    public int MessageRateLimitPerSecond { get; set; } = 100;
    
    /// <summary>
    /// Buffer size for outgoing messages per connection
    /// </summary>
    public int OutgoingBufferSize { get; set; } = 1000;
    
    /// <summary>
    /// Timeout for device authentication in seconds
    /// </summary>
    public int AuthenticationTimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Interval for sending ping messages in seconds
    /// </summary>
    public int PingIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// Timeout for ping response in seconds
    /// </summary>
    public int PingTimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Configuration for NATS connection
/// </summary>
public class NatsOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Nats";
    
    /// <summary>
    /// NATS server URL
    /// </summary>
    public string Url { get; set; } = "nats://localhost:4222";
    
    /// <summary>
    /// Name of this client for NATS
    /// </summary>
    public string ClientName { get; set; } = "NatsWebSocketBridge";
    
    /// <summary>
    /// JetStream stream name for device messages
    /// </summary>
    public string StreamName { get; set; } = "DEVICES";
    
    /// <summary>
    /// Whether to use JetStream
    /// </summary>
    public bool UseJetStream { get; set; } = true;
    
    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;
    
    /// <summary>
    /// Reconnect delay in milliseconds
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// Maximum reconnect attempts
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = -1; // Infinite
}
