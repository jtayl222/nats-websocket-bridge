using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Moq;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;

namespace NatsWebSocketBridge.Tests;

public class DeviceConnectionManagerTests
{
    private readonly DeviceConnectionManager _connectionManager;
    
    public DeviceConnectionManagerTests()
    {
        var logger = Mock.Of<ILogger<DeviceConnectionManager>>();
        _connectionManager = new DeviceConnectionManager(logger);
    }
    
    [Fact]
    public void RegisterConnection_AddsDevice()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        
        // Act
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Assert
        Assert.Equal(1, _connectionManager.ConnectionCount);
        Assert.True(_connectionManager.IsConnected("device-001"));
    }
    
    [Fact]
    public void RemoveConnection_RemovesDevice()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        _connectionManager.RemoveConnection("device-001");
        
        // Assert
        Assert.Equal(0, _connectionManager.ConnectionCount);
        Assert.False(_connectionManager.IsConnected("device-001"));
    }
    
    [Fact]
    public void GetConnection_ReturnsWebSocket()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        var result = _connectionManager.GetConnection("device-001");
        
        // Assert
        Assert.NotNull(result);
        Assert.Same(webSocket, result);
    }
    
    [Fact]
    public void GetConnection_UnknownDevice_ReturnsNull()
    {
        // Act
        var result = _connectionManager.GetConnection("unknown-device");
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void GetDeviceInfo_ReturnsDeviceInfo()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        var result = _connectionManager.GetDeviceInfo("device-001");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("device-001", result.DeviceId);
    }
    
    [Fact]
    public void GetConnectedDevices_ReturnsAllDeviceIds()
    {
        // Arrange
        _connectionManager.RegisterConnection("device-001", CreateDevice("device-001"), CreateMockWebSocket());
        _connectionManager.RegisterConnection("device-002", CreateDevice("device-002"), CreateMockWebSocket());
        _connectionManager.RegisterConnection("device-003", CreateDevice("device-003"), CreateMockWebSocket());
        
        // Act
        var devices = _connectionManager.GetConnectedDevices().ToList();
        
        // Assert
        Assert.Equal(3, devices.Count);
        Assert.Contains("device-001", devices);
        Assert.Contains("device-002", devices);
        Assert.Contains("device-003", devices);
    }
    
    [Fact]
    public void UpdateLastActivity_UpdatesTimestamp()
    {
        // Arrange
        var device = CreateDevice("device-001");
        device.LastActivityAt = DateTime.UtcNow.AddMinutes(-5);
        var originalTime = device.LastActivityAt;
        
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        _connectionManager.UpdateLastActivity("device-001");
        var updatedDevice = _connectionManager.GetDeviceInfo("device-001");
        
        // Assert
        Assert.NotNull(updatedDevice);
        Assert.True(updatedDevice.LastActivityAt > originalTime);
    }
    
    [Fact]
    public void RegisterConnection_ReplacesExistingConnection()
    {
        // Arrange
        var device1 = CreateDevice("device-001");
        var device2 = CreateDevice("device-001");
        device2.DeviceType = "actuator";
        
        var webSocket1 = CreateMockWebSocket();
        var webSocket2 = CreateMockWebSocket();
        
        // Act
        _connectionManager.RegisterConnection("device-001", device1, webSocket1);
        _connectionManager.RegisterConnection("device-001", device2, webSocket2);
        
        // Assert
        Assert.Equal(1, _connectionManager.ConnectionCount);
        var info = _connectionManager.GetDeviceInfo("device-001");
        Assert.Equal("actuator", info?.DeviceType);
    }
    
    private static DeviceInfo CreateDevice(string deviceId)
    {
        return new DeviceInfo
        {
            DeviceId = deviceId,
            DeviceType = "sensor",
            IsConnected = true,
            ConnectedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            AllowedPublishTopics = new List<string> { $"devices.{deviceId}.data" },
            AllowedSubscribeTopics = new List<string> { $"devices.{deviceId}.commands" }
        };
    }
    
    private static WebSocket CreateMockWebSocket()
    {
        var mock = new Mock<WebSocket>();
        mock.Setup(x => x.State).Returns(WebSocketState.Open);
        return mock.Object;
    }
}
