using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;
using NUnit.Framework;

namespace NatsWebSocketBridge.Tests;

public class MessageBufferServiceTests
{
    private MessageBufferService _bufferService = null!;

    [SetUp]
    public void SetUp()
    {
        var logger = Mock.Of<ILogger<MessageBufferService>>();
        var options = Options.Create(new GatewayOptions { OutgoingBufferSize = 10 });
        _bufferService = new MessageBufferService(logger, options);
    }
    
    [Test]
    public void CreateBuffer_CreatesBufferForDevice()
    {
        // Act
        _bufferService.CreateBuffer("device-001");
        var reader = _bufferService.GetReader("device-001");
        
        // Assert
        Assert.That(reader, Is.Not.Null);
    }
    
    [Test]
    public void Enqueue_WithNoBuffer_ReturnsFalse()
    {
        // Arrange
        var message = CreateMessage();
        
        // Act
        var result = _bufferService.Enqueue("unknown-device", message);
        
        // Assert
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void Enqueue_WithBuffer_ReturnsTrue()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        var message = CreateMessage();
        
        // Act
        var result = _bufferService.Enqueue("device-001", message);
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
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
        Assert.That(size, Is.EqualTo(3));
    }
    
    [Test]
    public void GetQueueSize_UnknownDevice_ReturnsZero()
    {
        // Act
        var size = _bufferService.GetQueueSize("unknown-device");
        
        // Assert
        Assert.That(size, Is.EqualTo(0));
    }
    
    [Test]
    public void RemoveBuffer_RemovesBuffer()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        
        // Act
        _bufferService.RemoveBuffer("device-001");
        var reader = _bufferService.GetReader("device-001");
        
        // Assert
        Assert.That(reader, Is.Null);
    }
    
    [Test]
    public async Task Reader_ReceivesEnqueuedMessages()
    {
        // Arrange
        _bufferService.CreateBuffer("device-001");
        var message = CreateMessage();
        message.Subject = "test.subject";
        
        // Act
        _bufferService.Enqueue("device-001", message);
        
        var reader = _bufferService.GetReader("device-001");
        Assert.That(reader, Is.Not.Null);
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var receivedMessage = await reader.ReadAsync(cts.Token);
        
        // Assert
        Assert.That(receivedMessage.Subject, Is.EqualTo("test.subject"));
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
