using Microsoft.Extensions.Logging;
using Moq;
using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Tests;

public class DeviceAuthorizationServiceTests
{
    private readonly DeviceAuthorizationService _authzService;
    
    public DeviceAuthorizationServiceTests()
    {
        var logger = Mock.Of<ILogger<DeviceAuthorizationService>>();
        _authzService = new DeviceAuthorizationService(logger);
    }
    
    [Fact]
    public void CanPublish_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var device = CreateDevice("devices.sensor-001.data");
        
        // Act
        var result = _authzService.CanPublish(device, "devices.sensor-001.data");
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void CanPublish_WithNoMatch_ReturnsFalse()
    {
        // Arrange
        var device = CreateDevice("devices.sensor-001.data");
        
        // Act
        var result = _authzService.CanPublish(device, "devices.sensor-002.data");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void CanPublish_WithWildcard_MatchesSingleToken()
    {
        // Arrange
        var device = CreateDevice("devices.*.data");
        
        // Act & Assert
        Assert.True(_authzService.CanPublish(device, "devices.sensor-001.data"));
        Assert.True(_authzService.CanPublish(device, "devices.sensor-002.data"));
        Assert.False(_authzService.CanPublish(device, "devices.sensor-001.status"));
    }
    
    [Fact]
    public void CanPublish_WithGreaterThan_MatchesMultipleTokens()
    {
        // Arrange
        var device = CreateDevice("devices.>");
        
        // Act & Assert
        Assert.True(_authzService.CanPublish(device, "devices.sensor-001"));
        Assert.True(_authzService.CanPublish(device, "devices.sensor-001.data"));
        Assert.True(_authzService.CanPublish(device, "devices.sensor-001.data.temperature"));
        Assert.False(_authzService.CanPublish(device, "other.sensor-001"));
    }
    
    [Fact]
    public void CanSubscribe_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var device = CreateDeviceForSubscribe("devices.sensor-001.commands");
        
        // Act
        var result = _authzService.CanSubscribe(device, "devices.sensor-001.commands");
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void CanSubscribe_WithWildcard_MatchesSingleToken()
    {
        // Arrange
        var device = CreateDeviceForSubscribe("devices.*.commands");
        
        // Act & Assert
        Assert.True(_authzService.CanSubscribe(device, "devices.sensor-001.commands"));
        Assert.True(_authzService.CanSubscribe(device, "devices.actuator-001.commands"));
        Assert.False(_authzService.CanSubscribe(device, "devices.sensor-001.data"));
    }
    
    [Fact]
    public void CanPublish_WithNullDevice_ReturnsFalse()
    {
        // Act
        var result = _authzService.CanPublish(null!, "some.subject");
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void CanPublish_WithEmptySubject_ReturnsFalse()
    {
        // Arrange
        var device = CreateDevice("devices.>");
        
        // Act
        var result = _authzService.CanPublish(device, "");
        
        // Assert
        Assert.False(result);
    }
    
    [Theory]
    [InlineData("devices.sensor-001.data", "devices.sensor-001.data", true)]
    [InlineData("devices.*.data", "devices.sensor-001.data", true)]
    [InlineData("devices.>", "devices.sensor-001.data", true)]
    [InlineData("devices.sensor-001.>", "devices.sensor-001.data.temp", true)]
    [InlineData("devices.sensor-001.data", "devices.sensor-002.data", false)]
    [InlineData("devices.*.data", "devices.sensor-001.status", false)]
    [InlineData("devices.sensor-001", "devices.sensor-001.data", false)]
    public void CanPublish_VariousPatterns(string pattern, string subject, bool expected)
    {
        // Arrange
        var device = CreateDevice(pattern);
        
        // Act
        var result = _authzService.CanPublish(device, subject);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    private static DeviceInfo CreateDevice(string publishTopic)
    {
        return new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceType = "sensor",
            AllowedPublishTopics = new List<string> { publishTopic },
            AllowedSubscribeTopics = new List<string>()
        };
    }
    
    private static DeviceInfo CreateDeviceForSubscribe(string subscribeTopic)
    {
        return new DeviceInfo
        {
            DeviceId = "test-device",
            DeviceType = "sensor",
            AllowedPublishTopics = new List<string>(),
            AllowedSubscribeTopics = new List<string> { subscribeTopic }
        };
    }
}
