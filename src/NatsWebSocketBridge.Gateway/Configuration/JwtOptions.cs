namespace NatsWebSocketBridge.Gateway.Configuration;

/// <summary>
/// JWT authentication configuration
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Secret key for signing/validating tokens (min 32 characters)
    /// </summary>
    public string Secret { get; set; } = "ThisIsADevelopmentSecretKeyThatShouldBeReplacedInProduction!";

    /// <summary>
    /// Token issuer (optional)
    /// </summary>
    public string? Issuer { get; set; } = "nats-gateway";

    /// <summary>
    /// Token audience (optional)
    /// </summary>
    public string? Audience { get; set; } = "nats-devices";

    /// <summary>
    /// Default token expiry in hours
    /// </summary>
    public int DefaultExpiryHours { get; set; } = 24;

    /// <summary>
    /// Clock skew tolerance in seconds
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
