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

    public void RegisterConnection(DeviceContext context, WebSocket webSocket)
    {
        var connection = new DeviceConnection(context, webSocket);
        _connections.AddOrUpdate(context.ClientId, connection, (_, _) => connection);
        _logger.LogInformation(
            "Device {ClientId} ({Role}) registered. Total connections: {Count}",
            context.ClientId, context.Role, _connections.Count);
    }

    public void RemoveConnection(string clientId)
    {
        if (_connections.TryRemove(clientId, out _))
        {
            _logger.LogInformation(
                "Device {ClientId} removed. Total connections: {Count}",
                clientId, _connections.Count);
        }
    }

    public WebSocket? GetConnection(string clientId)
    {
        return _connections.TryGetValue(clientId, out var connection) ? connection.WebSocket : null;
    }

    public DeviceContext? GetDeviceContext(string clientId)
    {
        return _connections.TryGetValue(clientId, out var connection) ? connection.Context : null;
    }

    public IEnumerable<string> GetConnectedDevices()
    {
        return _connections.Keys.ToList();
    }

    public bool IsConnected(string clientId)
    {
        return _connections.TryGetValue(clientId, out var connection)
            && connection.WebSocket.State == WebSocketState.Open;
    }

    private sealed record DeviceConnection(DeviceContext Context, WebSocket WebSocket);
}
