using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Gateway.Configuration;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Token bucket rate limiting for device messages
/// </summary>
public class TokenBucketThrottlingService : IMessageThrottlingService
{
    private readonly ILogger<TokenBucketThrottlingService> _logger;
    private readonly GatewayOptions _options;
    private readonly ConcurrentDictionary<string, DeviceBucket> _buckets = new();
    
    public TokenBucketThrottlingService(
        ILogger<TokenBucketThrottlingService> logger,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }
    
    public bool TryAcquire(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }
        
        var bucket = _buckets.GetOrAdd(deviceId, _ => new DeviceBucket(_options.MessageRateLimitPerSecond));
        var result = bucket.TryAcquire();
        
        if (!result)
        {
            _logger.LogWarning("Device {DeviceId} rate limited", deviceId);
        }
        
        return result;
    }
    
    public int GetCurrentCount(string deviceId)
    {
        if (_buckets.TryGetValue(deviceId, out var bucket))
        {
            return bucket.GetCurrentCount();
        }
        return 0;
    }
    
    public void Reset(string deviceId)
    {
        if (_buckets.TryGetValue(deviceId, out var bucket))
        {
            bucket.Reset();
        }
    }
    
    private class DeviceBucket
    {
        private readonly int _maxTokens;
        private readonly object _lock = new();
        private int _tokens;
        private DateTime _lastRefill;
        
        public DeviceBucket(int maxTokensPerSecond)
        {
            _maxTokens = maxTokensPerSecond;
            _tokens = maxTokensPerSecond;
            _lastRefill = DateTime.UtcNow;
        }
        
        public bool TryAcquire()
        {
            lock (_lock)
            {
                RefillTokens();
                
                if (_tokens > 0)
                {
                    _tokens--;
                    return true;
                }
                
                return false;
            }
        }
        
        public int GetCurrentCount()
        {
            lock (_lock)
            {
                return _maxTokens - _tokens;
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _tokens = _maxTokens;
                _lastRefill = DateTime.UtcNow;
            }
        }
        
        private void RefillTokens()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefill;
            
            if (elapsed.TotalSeconds >= 1)
            {
                _tokens = _maxTokens;
                _lastRefill = now;
            }
        }
    }
}
