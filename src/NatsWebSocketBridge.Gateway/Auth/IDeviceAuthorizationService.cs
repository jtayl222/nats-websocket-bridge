using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Auth;

/// <summary>
/// Interface for device authorization
/// </summary>
public interface IDeviceAuthorizationService
{
    /// <summary>
    /// Check if a device can publish to a subject
    /// </summary>
    /// <param name="device">Device info</param>
    /// <param name="subject">NATS subject</param>
    /// <returns>True if authorized</returns>
    bool CanPublish(DeviceInfo device, string subject);
    
    /// <summary>
    /// Check if a device can subscribe to a subject
    /// </summary>
    /// <param name="device">Device info</param>
    /// <param name="subject">NATS subject</param>
    /// <returns>True if authorized</returns>
    bool CanSubscribe(DeviceInfo device, string subject);
}
