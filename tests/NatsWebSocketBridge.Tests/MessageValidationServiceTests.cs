using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;

namespace NatsWebSocketBridge.Tests;

public class MessageValidationServiceTests
{
    private readonly MessageValidationService _validationService;
    
    public MessageValidationServiceTests()
    {
        var logger = Mock.Of<ILogger<MessageValidationService>>();
        var options = Options.Create(new GatewayOptions { MaxMessageSize = 1024 });
        _validationService = new MessageValidationService(logger, options);
    }
    
    [Fact]
    public void Validate_ValidPublishMessage_ReturnsSuccess()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Publish,
            Subject = "devices.sensor-001.data",
            Payload = new { temperature = 25.5 }
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public void Validate_NullMessage_ReturnsFailure()
    {
        // Act
        var result = _validationService.Validate(null!);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Message cannot be null", result.ErrorMessage);
    }
    
    [Fact]
    public void Validate_MissingSubject_ReturnsFailure()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Publish,
            Subject = "",
            Payload = new { data = "test" }
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Subject is required", result.ErrorMessage);
    }
    
    [Fact]
    public void Validate_PingMessage_DoesNotRequireSubject()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Ping
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public void Validate_AuthMessage_DoesNotRequireSubject()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Auth,
            Payload = new { deviceId = "test", token = "token" }
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Theory]
    [InlineData(".invalid")]
    [InlineData("invalid.")]
    [InlineData("invalid..subject")]
    public void Validate_InvalidSubjectFormat_ReturnsFailure(string subject)
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Publish,
            Subject = subject
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Invalid subject format", result.ErrorMessage);
    }
    
    [Theory]
    [InlineData("devices.sensor-001.data")]
    [InlineData("devices.*.data")]
    [InlineData("devices.>")]
    [InlineData("simple")]
    public void Validate_ValidSubjectFormats_ReturnsSuccess(string subject)
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Publish,
            Subject = subject
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.True(result.IsValid);
    }
    
    [Fact]
    public void Validate_SubjectTooLong_ReturnsFailure()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = MessageType.Publish,
            Subject = new string('a', 257) // 257 characters
        };
        
        // Act
        var result = _validationService.Validate(message);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("maximum length", result.ErrorMessage);
    }
}
