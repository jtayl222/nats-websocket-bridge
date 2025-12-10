namespace NatsWebSocketBridge.Gateway.Models;

/// <summary>
/// Device context extracted from JWT claims.
/// Immutable record containing identity and permissions.
/// </summary>
public sealed record DeviceContext
{
    /// <summary>
    /// Device identifier (from JWT "sub" claim)
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Device role (from JWT "role" claim)
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Topics the device can publish to (from JWT "pub" claim)
    /// </summary>
    public required IReadOnlyList<string> AllowedPublish { get; init; }

    /// <summary>
    /// Topics the device can subscribe to (from JWT "subscribe" claim)
    /// </summary>
    public required IReadOnlyList<string> AllowedSubscribe { get; init; }

    /// <summary>
    /// JWT expiration time
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// When this context was created (connection time)
    /// </summary>
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Check if the token has expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}

/// <summary>
/// Custom JWT claim names for NATS permissions
/// </summary>
public static class NatsJwtClaims
{
    public const string Role = "role";
    public const string Publish = "pub";
    public const string Subscribe = "subscribe";
}
