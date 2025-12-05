using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Manages WebSocket connections for devices
/// </summary>
public class DeviceConnectionManager : IDeviceConnectionManager
{
    private readonly ILogger<DeviceConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, DeviceConnection> _connections = new();
    
    public int ConnectionCount => _connections.Count;
    
    public DeviceConnectionManager(ILogger<DeviceConnectionManager> logger)
    {
        _logger = logger;
    }
    
    public void RegisterConnection(string deviceId, DeviceInfo device, WebSocket webSocket)
    {
        var connection = new DeviceConnection
        {
            DeviceInfo = device,
            WebSocket = webSocket
        };
        
        _connections.AddOrUpdate(deviceId, connection, (_, _) => connection);
        _logger.LogInformation("Device {DeviceId} registered. Total connections: {Count}", deviceId, _connections.Count);
    }
    
    public void RemoveConnection(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.DeviceInfo.IsConnected = false;
            _logger.LogInformation("Device {DeviceId} removed. Total connections: {Count}", deviceId, _connections.Count);
        }
    }
    
    public WebSocket? GetConnection(string deviceId)
    {
        return _connections.TryGetValue(deviceId, out var connection) ? connection.WebSocket : null;
    }
    
    public DeviceInfo? GetDeviceInfo(string deviceId)
    {
        return _connections.TryGetValue(deviceId, out var connection) ? connection.DeviceInfo : null;
    }
    
    public IEnumerable<string> GetConnectedDevices()
    {
        return _connections.Keys.ToList();
    }
    
    public bool IsConnected(string deviceId)
    {
        return _connections.TryGetValue(deviceId, out var connection) 
            && connection.WebSocket.State == WebSocketState.Open;
    }
    
    public void UpdateLastActivity(string deviceId)
    {
        if (_connections.TryGetValue(deviceId, out var connection))
        {
            connection.DeviceInfo.LastActivityAt = DateTime.UtcNow;
        }
    }
    
    private class DeviceConnection
    {
        public DeviceInfo DeviceInfo { get; set; } = null!;
        public WebSocket WebSocket { get; set; } = null!;
    }
}
