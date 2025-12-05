using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Interface for message validation
/// </summary>
public interface IMessageValidationService
{
    /// <summary>
    /// Validate a gateway message
    /// </summary>
    /// <param name="message">Message to validate</param>
    /// <returns>Validation result with success status and error message</returns>
    ValidationResult Validate(GatewayMessage message);
}

/// <summary>
/// Result of message validation
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Error message if validation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Create a successful validation result
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };
    
    /// <summary>
    /// Create a failed validation result
    /// </summary>
    public static ValidationResult Failure(string errorMessage) => new() { IsValid = false, ErrorMessage = errorMessage };
}
