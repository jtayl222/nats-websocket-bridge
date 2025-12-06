using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;

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
    
    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Device);
        Assert.Equal("sensor-temp-001", result.Device.DeviceId);
        Assert.Equal("sensor", result.Device.DeviceType);
        Assert.True(result.Device.IsConnected);
    }
    
    [Fact]
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
        Assert.False(result.Success);
        Assert.Null(result.Device);
        Assert.Equal("Invalid token", result.Error);
    }
    
    [Fact]
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
        Assert.False(result.Success);
        Assert.Null(result.Device);
        Assert.Equal("Device not registered", result.Error);
    }
    
    [Fact]
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
        Assert.False(result.Success);
        Assert.Equal("Device ID and token are required", result.Error);
    }
    
    [Fact]
    public async Task ValidateDeviceAsync_WithKnownDevice_ReturnsTrue()
    {
        // Act
        var result = await _authService.ValidateDeviceAsync("sensor-temp-001");
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public async Task ValidateDeviceAsync_WithUnknownDevice_ReturnsFalse()
    {
        // Act
        var result = await _authService.ValidateDeviceAsync("unknown-device");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Device);
        Assert.Contains("devices.sensor-temp-001.data", result.Device.AllowedPublishTopics);
        Assert.Contains("devices.sensor-temp-001.commands", result.Device.AllowedSubscribeTopics);
    }
}
