using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Message validation service
/// </summary>
public class MessageValidationService : IMessageValidationService
{
    private readonly ILogger<MessageValidationService> _logger;
    private readonly GatewayOptions _options;
    
    // Valid NATS subject pattern: alphanumeric, dots, wildcards
    private static readonly char[] ValidSubjectChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._->*".ToCharArray();
    
    public MessageValidationService(
        ILogger<MessageValidationService> logger,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }
    
    public ValidationResult Validate(GatewayMessage message)
    {
        if (message == null)
        {
            return ValidationResult.Failure("Message cannot be null");
        }
        
        // Validate message type
        if (!Enum.IsDefined(typeof(MessageType), message.Type))
        {
            return ValidationResult.Failure($"Invalid message type: {message.Type}");
        }
        
        // Ping/Pong don't need subject validation
        if (message.Type == MessageType.Ping || message.Type == MessageType.Pong)
        {
            return ValidationResult.Success();
        }
        
        // Auth messages don't need subject validation
        if (message.Type == MessageType.Auth)
        {
            return ValidationResult.Success();
        }
        
        // Validate subject for other message types
        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            return ValidationResult.Failure("Subject is required");
        }
        
        // Validate subject format
        if (!IsValidSubject(message.Subject))
        {
            return ValidationResult.Failure($"Invalid subject format: {message.Subject}");
        }
        
        // Validate subject length
        if (message.Subject.Length > 256)
        {
            return ValidationResult.Failure("Subject exceeds maximum length of 256 characters");
        }
        
        // Validate payload size (if present)
        if (message.Payload != null)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(message.Payload);
                if (payloadJson.Length > _options.MaxMessageSize)
                {
                    return ValidationResult.Failure($"Payload exceeds maximum size of {_options.MaxMessageSize} bytes");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to serialize payload for size check");
                return ValidationResult.Failure("Invalid payload format");
            }
        }
        
        return ValidationResult.Success();
    }
    
    private static bool IsValidSubject(string subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return false;
        }
        
        // Subject cannot start or end with a dot
        if (subject.StartsWith('.') || subject.EndsWith('.'))
        {
            return false;
        }
        
        // Subject cannot have consecutive dots
        if (subject.Contains(".."))
        {
            return false;
        }
        
        // All characters must be valid
        return subject.All(c => ValidSubjectChars.Contains(c));
    }
}
