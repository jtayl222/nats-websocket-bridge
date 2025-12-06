using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Moq;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;
using NUnit.Framework;

namespace NatsWebSocketBridge.Tests;

public class DeviceConnectionManagerTests
{
    private DeviceConnectionManager _connectionManager = null!;

    [SetUp]
    public void SetUp()
    {
        var logger = Mock.Of<ILogger<DeviceConnectionManager>>();
        _connectionManager = new DeviceConnectionManager(logger);
    }
    
    [Test]
    public void RegisterConnection_AddsDevice()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        
        // Act
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Assert
        Assert.That(_connectionManager.ConnectionCount, Is.EqualTo(1));
        Assert.That(_connectionManager.IsConnected("device-001"), Is.True);
    }
    
    [Test]
    public void RemoveConnection_RemovesDevice()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        _connectionManager.RemoveConnection("device-001");
        
        // Assert
        Assert.That(_connectionManager.ConnectionCount, Is.EqualTo(0));
        Assert.That(_connectionManager.IsConnected("device-001"), Is.False);
    }
    
    [Test]
    public void GetConnection_ReturnsWebSocket()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        var result = _connectionManager.GetConnection("device-001");
        
        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result , Is.SameAs(webSocket));
    }
    
    [Test]
    public void GetConnection_UnknownDevice_ReturnsNull()
    {
        // Act
        var result = _connectionManager.GetConnection("unknown-device");
        
        // Assert
        Assert.That(result, Is.Null);
    }
    
    [Test]
    public void GetDeviceInfo_ReturnsDeviceInfo()
    {
        // Arrange
        var device = CreateDevice("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection("device-001", device, webSocket);
        
        // Act
        var result = _connectionManager.GetDeviceInfo("device-001");
        
        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.DeviceId, Is.EqualTo("device-001"));
    }
    
    [Test]
    public void GetConnectedDevices_ReturnsAllDeviceIds()
    {
        // Arrange
        _connectionManager.RegisterConnection("device-001", CreateDevice("device-001"), CreateMockWebSocket());
        _connectionManager.RegisterConnection("device-002", CreateDevice("device-002"), CreateMockWebSocket());
        _connectionManager.RegisterConnection("device-003", CreateDevice("device-003"), CreateMockWebSocket());
        
        // Act
        var devices = _connectionManager.GetConnectedDevices().ToList();
        
        // Assert
        Assert.That(devices, Does.Contain("device-001"));
        Assert.That(devices, Does.Contain("device-002"));
        Assert.That(devices, Does.Contain("device-003"));
        Assert.That(devices.Count, Is.EqualTo(3));
    }
    
    [Test]
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
        Assert.That(updatedDevice, Is.Not.Null);
        Assert.That(updatedDevice.LastActivityAt > originalTime, Is.True);
    }
    
    [Test]
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
        Assert.That(_connectionManager.ConnectionCount, Is.EqualTo(1));
        var info = _connectionManager.GetDeviceInfo("device-001");
        Assert.That(info?.DeviceType, Is.EqualTo("actuator"));
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
