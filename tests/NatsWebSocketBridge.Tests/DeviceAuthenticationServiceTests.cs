using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;
using NUnit.Framework;

namespace NatsWebSocketBridge.Tests;

public class DeviceAuthenticationServiceTests
{
    private readonly InMemoryDeviceAuthenticationService _authService;
    
    public DeviceAuthenticationServiceTests()
    {
        var logger = Mock.Of<ILogger<InMemoryDeviceAuthenticationService>>();
        var options = Options.Create(new GatewayOptions());
        _authService = new InMemoryDeviceAuthenticationService(logger, options);
    }
    
    [Test]
    public async Task AuthenticateAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var request = new AuthenticationRequest
        {
            DeviceId = "sensor-temp-001",
            Token = "temp-sensor-token-001",
            DeviceType = "sensor"
        };
        
        // Act
        var result = await _authService.AuthenticateAsync(request);
        
        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Device, Is.Not.Null);
        Assert.That(result.Device.DeviceId, Is.EqualTo("sensor-temp-001"));
        Assert.That(result.Device.DeviceType, Is.EqualTo("sensor"));
        Assert.That(result.Device.IsConnected, Is.True);
    }
    
    [Test]
    public async Task AuthenticateAsync_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var request = new AuthenticationRequest
        {
            DeviceId = "sensor-temp-001",
            Token = "wrong-token",
            DeviceType = "sensor"
        };
        
        // Act
        var result = await _authService.AuthenticateAsync(request);
        
        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Device, Is.Null);
        Assert.That(result.Error, Is.EqualTo("Invalid token"));
    }
    
    [Test]
    public async Task AuthenticateAsync_WithUnknownDevice_ReturnsFailure()
    {
        // Arrange
        var request = new AuthenticationRequest
        {
            DeviceId = "unknown-device",
            Token = "some-token",
            DeviceType = "sensor"
        };
        
        // Act
        var result = await _authService.AuthenticateAsync(request);
        
        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Device, Is.Null);
        Assert.That(result.Error, Is.EqualTo("Device not registered"));
    }
    
    [Test]
    public async Task AuthenticateAsync_WithEmptyDeviceId_ReturnsFailure()
    {
        // Arrange
        var request = new AuthenticationRequest
        {
            DeviceId = "",
            Token = "some-token"
        };
        
        // Act
        var result = await _authService.AuthenticateAsync(request);
        
        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Device ID and token are required"));
    }
    
    [Test]
    public async Task ValidateDeviceAsync_WithKnownDevice_ReturnsTrue()
    {
        // Act
        var result = await _authService.ValidateDeviceAsync("sensor-temp-001");
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public async Task ValidateDeviceAsync_WithUnknownDevice_ReturnsFalse()
    {
        // Act
        var result = await _authService.ValidateDeviceAsync("unknown-device");
        
        // Assert
        Assert.That(result, Is.False);
    }
    
    [Test]
    public async Task AuthenticateAsync_SetsAllowedTopics()
    {
        // Arrange
        var request = new AuthenticationRequest
        {
            DeviceId = "sensor-temp-001",
            Token = "temp-sensor-token-001"
        };
        
        // Act
        var result = await _authService.AuthenticateAsync(request);
        
        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Device, Is.Not.Null);
        Assert.That(result.Device.AllowedPublishTopics, Does.Contain("devices.sensor-temp-001.data"));
        Assert.That(result.Device.AllowedSubscribeTopics, Does.Contain("devices.sensor-temp-001.commands"));
    }
}
