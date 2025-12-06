using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;

namespace NatsWebSocketBridge.Tests;

public class MessageBufferServiceTests
{
    private readonly MessageBufferService _bufferService;
    
    public MessageBufferServiceTests()
    {
        var logger = Mock.Of<ILogger<MessageBufferService>>();
        var options = Options.Create(new GatewayOptions { OutgoingBufferSize = 10 });
        _bufferService = new MessageBufferService(logger, options);
    }
    
    [Fact]
    public void CreateBuffer_CreatesBufferForDevice()
    {
        // Act
        _bufferService.CreateBuffer("device-001");
        var reader = _bufferService.GetReader("device-001");
        
        // Assert
        Assert.NotNull(reader);
    }
    
    [Fact]
    public void Enqueue_WithNoBuffer_ReturnsFalse()
    {
        // Arrange
        var message = CreateMessage();
        
        // Act
        var result = _bufferService.Enqueue("unknown-device", message);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void Enqueue_WithBuffer_ReturnsTrue()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        var message = CreateMessage();
        
        // Act
        var result = _bufferService.Enqueue("device-001", message);
        
        // Assert
        Assert.True(result);
    }
    
    [Fact]
    public void GetQueueSize_ReturnsCorrectCount()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        _bufferService.Enqueue("device-001", CreateMessage());
        _bufferService.Enqueue("device-001", CreateMessage());
        _bufferService.Enqueue("device-001", CreateMessage());
        
        // Act
        var size = _bufferService.GetQueueSize("device-001");
        
        // Assert
        Assert.Equal(3, size);
    }
    
    [Fact]
    public void GetQueueSize_UnknownDevice_ReturnsZero()
    {
        // Act
        var size = _bufferService.GetQueueSize("unknown-device");
        
        // Assert
        Assert.Equal(0, size);
    }
    
    [Fact]
    public void RemoveBuffer_RemovesBuffer()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        
        // Act
        _bufferService.RemoveBuffer("device-001");
        var reader = _bufferService.GetReader("device-001");
        
        // Assert
        Assert.Null(reader);
    }
    
    [Fact]
    public async Task Reader_ReceivesEnqueuedMessages()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        var message = CreateMessage();
        message.Subject = "test.subject";
        
        // Act
        _bufferService.Enqueue("device-001", message);
        
        var reader = _bufferService.GetReader("device-001");
        Assert.NotNull(reader);
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var receivedMessage = await reader.ReadAsync(cts.Token);
        
        // Assert
        Assert.Equal("test.subject", receivedMessage.Subject);
    }
    
    private static GatewayMessage CreateMessage()
    {
        return new GatewayMessage
        {
            Type = MessageType.Message,
            Subject = "devices.sensor-001.data",
            Payload = new { temperature = 25.5 },
            Timestamp = DateTime.UtcNow
        };
    }
}
