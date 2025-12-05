using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Auth;

/// <summary>
/// Interface for device authentication
/// </summary>
public interface IDeviceAuthenticationService
{
    /// <summary>
    /// Authenticate a device
    /// </summary>
    /// <param name="request">Authentication request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication response with device info if successful</returns>
    Task<AuthenticationResponse> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate if a device exists and is valid
    /// </summary>
    /// <param name="deviceId">Device ID to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if device is valid</returns>
    Task<bool> ValidateDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
