using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Auth;

/// <summary>
/// JWT-based device authentication and authorization service.
/// Validates JWT tokens and extracts device context with permissions.
/// </summary>
public class JwtDeviceAuthService : IJwtDeviceAuthService
{
    private readonly ILogger<JwtDeviceAuthService> _logger;
    private readonly JwtOptions _options;
    private readonly TokenValidationParameters _validationParameters;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public JwtDeviceAuthService(
        ILogger<JwtDeviceAuthService> logger,
        IOptions<JwtOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _tokenHandler = new JwtSecurityTokenHandler();

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrEmpty(_options.Issuer),
            ValidIssuer = _options.Issuer,
            ValidateAudience = !string.IsNullOrEmpty(_options.Audience),
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
            ClockSkew = TimeSpan.FromSeconds(_options.ClockSkewSeconds)
        };
    }

    /// <summary>
    /// Validate a JWT token and extract device context
    /// </summary>
    public DeviceAuthResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return DeviceAuthResult.Failure("Token is required");
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
            {
                return DeviceAuthResult.Failure("Invalid token format");
            }

            var context = ExtractDeviceContext(principal, jwtToken);
            if (context == null)
            {
                return DeviceAuthResult.Failure("Missing required claims");
            }

            _logger.LogInformation(
                "Device {ClientId} authenticated with role {Role}. Pub: {PubCount}, Sub: {SubCount}",
                context.ClientId, context.Role,
                context.AllowedPublish.Count, context.AllowedSubscribe.Count);

            return DeviceAuthResult.Success(context);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Token expired");
            return DeviceAuthResult.Failure("Token expired");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return DeviceAuthResult.Failure($"Token validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation");
            return DeviceAuthResult.Failure("Authentication error");
        }
    }

    /// <summary>
    /// Check if device can publish to subject
    /// </summary>
    public bool CanPublish(DeviceContext context, string subject)
    {
        if (context == null || string.IsNullOrEmpty(subject))
            return false;

        if (context.IsExpired)
        {
            _logger.LogWarning("Device {ClientId} token expired", context.ClientId);
            return false;
        }

        return context.AllowedPublish.Any(pattern => MatchesSubject(pattern, subject));
    }

    /// <summary>
    /// Check if device can subscribe to subject
    /// </summary>
    public bool CanSubscribe(DeviceContext context, string subject)
    {
        if (context == null || string.IsNullOrEmpty(subject))
            return false;

        if (context.IsExpired)
        {
            _logger.LogWarning("Device {ClientId} token expired", context.ClientId);
            return false;
        }

        return context.AllowedSubscribe.Any(pattern => MatchesSubject(pattern, subject));
    }

    /// <summary>
    /// Generate a JWT token for a device (for testing/development)
    /// </summary>
    public string GenerateToken(string clientId, string role, IEnumerable<string> publish, IEnumerable<string> subscribe, TimeSpan? expiry = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(NatsJwtClaims.Role, role),
            new(NatsJwtClaims.Publish, JsonSerializer.Serialize(publish.ToList())),
            new(NatsJwtClaims.Subscribe, JsonSerializer.Serialize(subscribe.ToList()))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(_options.DefaultExpiryHours)),
            signingCredentials: credentials
        );

        return _tokenHandler.WriteToken(token);
    }

    private DeviceContext? ExtractDeviceContext(ClaimsPrincipal principal, JwtSecurityToken token)
    {
        var clientId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Check both our custom claim and the role claim type (JWT handler may map it)
        var role = principal.FindFirst(NatsJwtClaims.Role)?.Value
                ?? principal.FindFirst(ClaimTypes.Role)?.Value
                ?? token.Claims.FirstOrDefault(c => c.Type == NatsJwtClaims.Role)?.Value
                ?? "device";

        // For custom claims, also check the token directly
        var publishClaim = principal.FindFirst(NatsJwtClaims.Publish)?.Value
                        ?? token.Claims.FirstOrDefault(c => c.Type == NatsJwtClaims.Publish)?.Value;
        var subscribeClaim = principal.FindFirst(NatsJwtClaims.Subscribe)?.Value
                          ?? token.Claims.FirstOrDefault(c => c.Type == NatsJwtClaims.Subscribe)?.Value;

        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("Token missing subject claim");
            return null;
        }

        var publish = ParseTopicList(publishClaim);
        var subscribe = ParseTopicList(subscribeClaim);

        return new DeviceContext
        {
            ClientId = clientId,
            Role = role,
            AllowedPublish = publish,
            AllowedSubscribe = subscribe,
            ExpiresAt = token.ValidTo
        };
    }

    private static IReadOnlyList<string> ParseTopicList(string? claim)
    {
        if (string.IsNullOrEmpty(claim))
            return Array.Empty<string>();

        try
        {
            // Try JSON array first: ["topic1", "topic2"]
            var list = JsonSerializer.Deserialize<List<string>>(claim);
            return list?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            // Fall back to comma-separated: "topic1,topic2"
            return claim.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>
    /// Match a NATS subject against a pattern with wildcards.
    /// '*' matches a single token, '>' matches one or more tokens at end.
    /// </summary>
    internal static bool MatchesSubject(string pattern, string subject)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(subject))
            return false;

        if (pattern == subject)
            return true;

        var patternParts = pattern.Split('.');
        var subjectParts = subject.Split('.');

        for (int i = 0; i < patternParts.Length; i++)
        {
            var part = patternParts[i];

            // '>' matches everything from here to end
            if (part == ">")
                return i < subjectParts.Length;

            if (i >= subjectParts.Length)
                return false;

            // '*' matches any single token
            if (part == "*")
                continue;

            if (part != subjectParts[i])
                return false;
        }

        return patternParts.Length == subjectParts.Length;
    }
}

/// <summary>
/// Result of device authentication
/// </summary>
public record DeviceAuthResult
{
    public bool IsSuccess { get; init; }
    public DeviceContext? Context { get; init; }
    public string? Error { get; init; }

    public static DeviceAuthResult Success(DeviceContext context) => new() { IsSuccess = true, Context = context };
    public static DeviceAuthResult Failure(string error) => new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Interface for JWT device authentication
/// </summary>
public interface IJwtDeviceAuthService
{
    DeviceAuthResult ValidateToken(string token);
    bool CanPublish(DeviceContext context, string subject);
    bool CanSubscribe(DeviceContext context, string subject);
    string GenerateToken(string clientId, string role, IEnumerable<string> publish, IEnumerable<string> subscribe, TimeSpan? expiry = null);
}
