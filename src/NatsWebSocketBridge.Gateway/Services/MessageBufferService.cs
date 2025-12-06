using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Buffers outgoing messages per device using channels
/// </summary>
public class MessageBufferService : IMessageBufferService
{
    private readonly ILogger<MessageBufferService> _logger;
    private readonly GatewayOptions _options;
    private readonly ConcurrentDictionary<string, Channel<GatewayMessage>> _buffers = new();
    
    public MessageBufferService(
        ILogger<MessageBufferService> logger,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }
    
    public void CreateBuffer(string deviceId)
    {
        var channel = Channel.CreateBounded<GatewayMessage>(new BoundedChannelOptions(_options.OutgoingBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        
        _buffers.TryAdd(deviceId, channel);
        _logger.LogDebug("Created message buffer for device {DeviceId}", deviceId);
    }
    
    public void RemoveBuffer(string deviceId)
    {
        if (_buffers.TryRemove(deviceId, out var channel))
        {
            channel.Writer.Complete();
            _logger.LogDebug("Removed message buffer for device {DeviceId}", deviceId);
        }
    }
    
    public bool Enqueue(string deviceId, GatewayMessage message)
    {
        if (!_buffers.TryGetValue(deviceId, out var channel))
        {
            _logger.LogWarning("No buffer exists for device {DeviceId}", deviceId);
            return false;
        }
        
        if (channel.Writer.TryWrite(message))
        {
            return true;
        }
        
        _logger.LogWarning("Message buffer full for device {DeviceId}", deviceId);
        return false;
    }
    
    public ChannelReader<GatewayMessage>? GetReader(string deviceId)
    {
        return _buffers.TryGetValue(deviceId, out var channel) ? channel.Reader : null;
    }
    
    public int GetQueueSize(string deviceId)
    {
        if (_buffers.TryGetValue(deviceId, out var channel))
        {
            return channel.Reader.Count;
        }
        return 0;
    }
}
