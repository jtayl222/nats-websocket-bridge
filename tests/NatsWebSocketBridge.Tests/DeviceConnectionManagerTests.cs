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
        var context = CreateContext("device-001");
        var webSocket = CreateMockWebSocket();

        // Act
        _connectionManager.RegisterConnection(context, webSocket);

        // Assert
        Assert.That(_connectionManager.ConnectionCount, Is.EqualTo(1));
        Assert.That(_connectionManager.IsConnected("device-001"), Is.True);
    }

    [Test]
    public void RemoveConnection_RemovesDevice()
    {
        // Arrange
        var context = CreateContext("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection(context, webSocket);

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
        var context = CreateContext("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection(context, webSocket);

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
    public void GetDeviceContext_ReturnsDeviceContext()
    {
        // Arrange
        var context = CreateContext("device-001");
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection(context, webSocket);

        // Act
        var result = _connectionManager.GetDeviceContext("device-001");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ClientId, Is.EqualTo("device-001"));
    }

    [Test]
    public void GetConnectedDevices_ReturnsAllDeviceIds()
    {
        // Arrange
        _connectionManager.RegisterConnection(CreateContext("device-001"), CreateMockWebSocket());
        _connectionManager.RegisterConnection(CreateContext("device-002"), CreateMockWebSocket());
        _connectionManager.RegisterConnection(CreateContext("device-003"), CreateMockWebSocket());

        // Act
        var devices = _connectionManager.GetConnectedDevices().ToList();

        // Assert
        Assert.That(devices, Does.Contain("device-001"));
        Assert.That(devices, Does.Contain("device-002"));
        Assert.That(devices, Does.Contain("device-003"));
        Assert.That(devices.Count, Is.EqualTo(3));
    }

    [Test]
    public void RegisterConnection_ReplacesExistingConnection()
    {
        // Arrange
        var context1 = CreateContext("device-001", "sensor");
        var context2 = CreateContext("device-001", "actuator");

        var webSocket1 = CreateMockWebSocket();
        var webSocket2 = CreateMockWebSocket();

        // Act
        _connectionManager.RegisterConnection(context1, webSocket1);
        _connectionManager.RegisterConnection(context2, webSocket2);

        // Assert
        Assert.That(_connectionManager.ConnectionCount, Is.EqualTo(1));
        var result = _connectionManager.GetDeviceContext("device-001");
        Assert.That(result?.Role, Is.EqualTo("actuator"));
    }

    [Test]
    public void GetDeviceContext_ReturnsContextWithCorrectProperties()
    {
        // Arrange
        var context = new DeviceContext
        {
            ClientId = "device-001",
            Role = "admin",
            AllowedPublish = new[] { "devices.>" },
            AllowedSubscribe = new[] { "devices.>" },
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        var webSocket = CreateMockWebSocket();
        _connectionManager.RegisterConnection(context, webSocket);

        // Act
        var result = _connectionManager.GetDeviceContext("device-001");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ClientId, Is.EqualTo("device-001"));
        Assert.That(result.Role, Is.EqualTo("admin"));
        Assert.That(result.AllowedPublish, Does.Contain("devices.>"));
        Assert.That(result.AllowedSubscribe, Does.Contain("devices.>"));
    }

    [Test]
    public void IsConnected_WithClosedWebSocket_ReturnsFalse()
    {
        // Arrange
        var context = CreateContext("device-001");
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.Setup(x => x.State).Returns(WebSocketState.Closed);
        _connectionManager.RegisterConnection(context, mockWebSocket.Object);

        // Act
        var result = _connectionManager.IsConnected("device-001");

        // Assert
        Assert.That(result, Is.False);
    }

    private static DeviceContext CreateContext(string clientId, string role = "sensor")
    {
        return new DeviceContext
        {
            ClientId = clientId,
            Role = role,
            AllowedPublish = new[] { $"devices.{clientId}.data" },
            AllowedSubscribe = new[] { $"devices.{clientId}.commands" },
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    private static WebSocket CreateMockWebSocket()
    {
        var mock = new Mock<WebSocket>();
        mock.Setup(x => x.State).Returns(WebSocketState.Open);
        return mock.Object;
    }
}
