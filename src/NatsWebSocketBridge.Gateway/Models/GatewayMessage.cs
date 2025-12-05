using System.Text.Json.Serialization;

namespace NatsWebSocketBridge.Gateway.Models;

/// <summary>
/// Represents a message sent between devices and NATS
/// </summary>
public class GatewayMessage
{
    /// <summary>
    /// Type of message operation
    /// </summary>
    [JsonPropertyName("type")]
    public MessageType Type { get; set; }
    
    /// <summary>
    /// NATS subject/topic for the message
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// Message payload as JSON
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
    
    /// <summary>
    /// Optional correlation ID for request/reply patterns
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// Timestamp when message was created
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Device ID that originated the message (set by gateway)
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}

/// <summary>
/// Types of gateway messages
/// </summary>
public enum MessageType
{
    /// <summary>
    /// Publish a message to a NATS subject
    /// </summary>
    Publish = 0,
    
    /// <summary>
    /// Subscribe to a NATS subject
    /// </summary>
    Subscribe = 1,
    
    /// <summary>
    /// Unsubscribe from a NATS subject
    /// </summary>
    Unsubscribe = 2,
    
    /// <summary>
    /// Message received from a subscription
    /// </summary>
    Message = 3,
    
    /// <summary>
    /// Request/reply message
    /// </summary>
    Request = 4,
    
    /// <summary>
    /// Reply to a request
    /// </summary>
    Reply = 5,
    
    /// <summary>
    /// Acknowledgment message
    /// </summary>
    Ack = 6,
    
    /// <summary>
    /// Error message
    /// </summary>
    Error = 7,
    
    /// <summary>
    /// Authentication message
    /// </summary>
    Auth = 8,
    
    /// <summary>
    /// Ping/keepalive message
    /// </summary>
    Ping = 9,
    
    /// <summary>
    /// Pong response to ping
    /// </summary>
    Pong = 10
}
