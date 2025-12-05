using System.Text.Json.Serialization;

namespace NatsWebSocketBridge.Gateway.Models;

/// <summary>
/// Authentication request from a device
/// </summary>
public class AuthenticationRequest
{
    /// <summary>
    /// Device identifier
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Authentication token or API key
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of device
    /// </summary>
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;
}

/// <summary>
/// Authentication response to a device
/// </summary>
public class AuthenticationResponse
{
    /// <summary>
    /// Whether authentication was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    /// <summary>
    /// Device info if authentication succeeded
    /// </summary>
    [JsonPropertyName("device")]
    public DeviceInfo? Device { get; set; }
}
