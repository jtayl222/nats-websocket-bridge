using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Services;
using NUnit.Framework;

namespace NatsWebSocketBridge.Tests;

public class TokenBucketThrottlingServiceTests
{
    private readonly TokenBucketThrottlingService _throttlingService;
    
    public TokenBucketThrottlingServiceTests()
    {
        var logger = Mock.Of<ILogger<TokenBucketThrottlingService>>();
        var options = Options.Create(new GatewayOptions { MessageRateLimitPerSecond = 5 });
        _throttlingService = new TokenBucketThrottlingService(logger, options);
    }
    
    [Test]
    public void TryAcquire_FirstRequest_ReturnsTrue()
    {
        // Act
        var result = _throttlingService.TryAcquire("device-001");
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void TryAcquire_WithinRateLimit_ReturnsTrue()
    {
        // Arrange
        var deviceId = "device-002";
        
        // Act - acquire 5 times (rate limit)
        for (int i = 0; i < 5; i++)
        {
            Assert.That(_throttlingService.TryAcquire(deviceId), Is.True);
        }
    }
    
    [Test]
    public void TryAcquire_ExceedsRateLimit_ReturnsFalse()
    {
        // Arrange
        var deviceId = "device-003";
        
        // Act - acquire 5 times to exhaust tokens
        for (int i = 0; i < 5; i++)
        {
            _throttlingService.TryAcquire(deviceId);
        }
        
        // 6th request should fail
        var result = _throttlingService.TryAcquire(deviceId);
        
        // Assert
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void TryAcquire_EmptyDeviceId_ReturnsFalse()
    {
        // Act
        var result = _throttlingService.TryAcquire("");
        
        // Assert
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void GetCurrentCount_AfterRequests_ReturnsCorrectCount()
    {
        // Arrange
        var deviceId = "device-004";
        
        // Act
        _throttlingService.TryAcquire(deviceId);
        _throttlingService.TryAcquire(deviceId);
        _throttlingService.TryAcquire(deviceId);
        
        var count = _throttlingService.GetCurrentCount(deviceId);
        
        // Assert
        Assert.That(count, Is.EqualTo(3));
    }
    
    [Test]
    public void Reset_ClearsTokensForDevice()
    {
        // Arrange
        var deviceId = "device-005";
        
        // Exhaust all tokens
        for (int i = 0; i < 5; i++)
        {
            _throttlingService.TryAcquire(deviceId);
        }
        
        // Verify rate limited
        Assert.That(_throttlingService.TryAcquire(deviceId), Is.False);
        
        // Act - reset
        _throttlingService.Reset(deviceId);
        
        // Assert - should be able to acquire again
        Assert.That(_throttlingService.TryAcquire(deviceId), Is.True);
    }
    
    [Test]
    public void TryAcquire_DifferentDevices_IndependentLimits()
    {
        // Arrange
        var device1 = "device-006";
        var device2 = "device-007";
        
        // Exhaust device1's tokens
        for (int i = 0; i < 5; i++)
        {
            _throttlingService.TryAcquire(device1);
        }
        
        // Act & Assert - device1 is rate limited
        Assert.That(_throttlingService.TryAcquire(device1), Is.False);
        
        // But device2 should still have tokens
        Assert.That(_throttlingService.TryAcquire(device2), Is.True);
    }
}
