namespace NatsWebSocketBridge.Gateway.Models;

/// <summary>
/// Represents a connected device in the gateway
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// Unique identifier for the device
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of device (e.g., "sensor", "actuator", "controller")
    /// </summary>
    public string DeviceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the device is currently connected
    /// </summary>
    public bool IsConnected { get; set; }
    
    /// <summary>
    /// Timestamp of when the device connected
    /// </summary>
    public DateTime ConnectedAt { get; set; }
    
    /// <summary>
    /// Timestamp of last activity from the device
    /// </summary>
    public DateTime LastActivityAt { get; set; }
    
    /// <summary>
    /// Topics the device is allowed to publish to
    /// </summary>
    public List<string> AllowedPublishTopics { get; set; } = new();
    
    /// <summary>
    /// Topics the device is allowed to subscribe to
    /// </summary>
    public List<string> AllowedSubscribeTopics { get; set; } = new();
}
