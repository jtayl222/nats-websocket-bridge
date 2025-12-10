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
    void RegisterConnection(DeviceContext context, WebSocket webSocket);

    /// <summary>
    /// Remove a device connection
    /// </summary>
    void RemoveConnection(string clientId);

    /// <summary>
    /// Get a device's WebSocket connection
    /// </summary>
    WebSocket? GetConnection(string clientId);

    /// <summary>
    /// Get device context
    /// </summary>
    DeviceContext? GetDeviceContext(string clientId);

    /// <summary>
    /// Get all connected device IDs
    /// </summary>
    IEnumerable<string> GetConnectedDevices();

    /// <summary>
    /// Check if a device is connected
    /// </summary>
    bool IsConnected(string clientId);

    /// <summary>
    /// Get the count of connected devices
    /// </summary>
    int ConnectionCount { get; }
}
