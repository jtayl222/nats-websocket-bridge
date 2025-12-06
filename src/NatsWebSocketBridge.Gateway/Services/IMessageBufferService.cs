using System.Threading.Channels;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Interface for buffering outgoing messages
/// </summary>
public interface IMessageBufferService
{
    /// <summary>
    /// Queue a message for sending to a device
    /// </summary>
    /// <param name="deviceId">Target device ID</param>
    /// <param name="message">Message to send</param>
    /// <returns>True if message was queued, false if buffer is full</returns>
    bool Enqueue(string deviceId, GatewayMessage message);
    
    /// <summary>
    /// Get the channel reader for a device's messages
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>Channel reader for messages</returns>
    ChannelReader<GatewayMessage>? GetReader(string deviceId);
    
    /// <summary>
    /// Create a buffer for a device
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    void CreateBuffer(string deviceId);
    
    /// <summary>
    /// Remove a device's buffer
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    void RemoveBuffer(string deviceId);
    
    /// <summary>
    /// Get the current queue size for a device
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <returns>Number of messages in queue</returns>
    int GetQueueSize(string deviceId);
}
