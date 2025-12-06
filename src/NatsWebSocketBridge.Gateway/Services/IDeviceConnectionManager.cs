using System.Net.WebSockets;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Interface for managing device connections
/// </summary>
public interface IDeviceConnectionManager
{
    /// <summary>
    /// Register a device connection
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="device">Device info</param>
    /// <param name="webSocket">WebSocket connection</param>
    void RegisterConnection(string deviceId, DeviceInfo device, WebSocket webSocket);
    
    /// <summary>
    /// Remove a device connection
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    void RemoveConnection(string deviceId);
    
    /// <summary>
    /// Get a device's WebSocket connection
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>WebSocket if connected, null otherwise</returns>
    WebSocket? GetConnection(string deviceId);
    
    /// <summary>
    /// Get device info
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>Device info if connected, null otherwise</returns>
    DeviceInfo? GetDeviceInfo(string deviceId);
    
    /// <summary>
    /// Get all connected device IDs
    /// </summary>
    /// <returns>List of connected device IDs</returns>
    IEnumerable<string> GetConnectedDevices();
    
    /// <summary>
    /// Check if a device is connected
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>True if connected</returns>
    bool IsConnected(string deviceId);
    
    /// <summary>
    /// Get the count of connected devices
    /// </summary>
    int ConnectionCount { get; }
    
    /// <summary>
    /// Update last activity time for a device
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    void UpdateLastActivity(string deviceId);
}
