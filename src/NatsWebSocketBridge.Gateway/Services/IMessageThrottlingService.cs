namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Interface for rate limiting messages
/// </summary>
public interface IMessageThrottlingService
{
    /// <summary>
    /// Check if a device can send a message (rate limiting)
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>True if the device can send, false if rate limited</returns>
    bool TryAcquire(string deviceId);
    
    /// <summary>
    /// Get the current message count for a device
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>Current message count in the window</returns>
    int GetCurrentCount(string deviceId);
    
    /// <summary>
    /// Reset the counter for a device
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    void Reset(string deviceId);
}
