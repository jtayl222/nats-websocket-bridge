using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Auth;

/// <summary>
/// Device authorization service that checks permissions based on NATS-style wildcards
/// </summary>
public class DeviceAuthorizationService : IDeviceAuthorizationService
{
    private readonly ILogger<DeviceAuthorizationService> _logger;
    
    public DeviceAuthorizationService(ILogger<DeviceAuthorizationService> logger)
    {
        _logger = logger;
    }
    
    public bool CanPublish(DeviceInfo device, string subject)
    {
        if (device == null || string.IsNullOrEmpty(subject))
        {
            return false;
        }
        
        var canPublish = device.AllowedPublishTopics.Any(pattern => MatchesSubject(pattern, subject));
        
        if (!canPublish)
        {
            _logger.LogWarning("Device {DeviceId} denied publish to {Subject}", device.DeviceId, subject);
        }
        
        return canPublish;
    }
    
    public bool CanSubscribe(DeviceInfo device, string subject)
    {
        if (device == null || string.IsNullOrEmpty(subject))
        {
            return false;
        }
        
        var canSubscribe = device.AllowedSubscribeTopics.Any(pattern => MatchesSubject(pattern, subject));
        
        if (!canSubscribe)
        {
            _logger.LogWarning("Device {DeviceId} denied subscribe to {Subject}", device.DeviceId, subject);
        }
        
        return canSubscribe;
    }
    
    /// <summary>
    /// Match a NATS subject against a pattern with wildcards
    /// '*' matches a single token
    /// '>' matches one or more tokens (must be at end)
    /// </summary>
    internal static bool MatchesSubject(string pattern, string subject)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(subject))
        {
            return false;
        }
        
        // Exact match
        if (pattern == subject)
        {
            return true;
        }
        
        var patternParts = pattern.Split('.');
        var subjectParts = subject.Split('.');
        
        for (int i = 0; i < patternParts.Length; i++)
        {
            var patternPart = patternParts[i];
            
            // '>' matches everything from here to end
            if (patternPart == ">")
            {
                // '>' must match at least one token
                return i < subjectParts.Length;
            }
            
            // If we've run out of subject parts, no match
            if (i >= subjectParts.Length)
            {
                return false;
            }
            
            // '*' matches any single token
            if (patternPart == "*")
            {
                continue;
            }
            
            // Exact token match required
            if (patternPart != subjectParts[i])
            {
                return false;
            }
        }
        
        // Pattern must have matched all subject parts
        return patternParts.Length == subjectParts.Length;
    }
}
