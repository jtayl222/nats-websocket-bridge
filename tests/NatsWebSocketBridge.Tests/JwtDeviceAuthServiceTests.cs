using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NUnit.Framework;

namespace NatsWebSocketBridge.Tests;

public class JwtDeviceAuthServiceTests
{
    private JwtDeviceAuthService _authService = null!;
    private JwtOptions _jwtOptions = null!;

    [SetUp]
    public void SetUp()
    {
        var logger = Mock.Of<ILogger<JwtDeviceAuthService>>();
        _jwtOptions = new JwtOptions
        {
            Secret = "TestSecretKeyThatIsAtLeast32CharactersLong!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            DefaultExpiryHours = 24,
            ClockSkewSeconds = 30
        };
        var options = Options.Create(_jwtOptions);
        _authService = new JwtDeviceAuthService(logger, options);
    }

    [Test]
    public void ValidateToken_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001",
            "sensor",
            new[] { "devices.device-001.data" },
            new[] { "devices.device-001.commands" });

        // Act
        var result = _authService.ValidateToken(token);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Context, Is.Not.Null);
        Assert.That(result.Context!.ClientId, Is.EqualTo("device-001"));
        Assert.That(result.Context.Role, Is.EqualTo("sensor"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void ValidateToken_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var token = "invalid-token";

        // Act
        var result = _authService.ValidateToken(token);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Context, Is.Null);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public void ValidateToken_WithEmptyToken_ReturnsFailure()
    {
        // Act
        var result = _authService.ValidateToken("");

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo("Token is required"));
    }

    [Test]
    public void ValidateToken_ExtractsAllowedTopics()
    {
        // Arrange
        var publishTopics = new[] { "devices.device-001.data", "devices.device-001.status" };
        var subscribeTopics = new[] { "devices.device-001.commands", "devices.*.broadcasts" };
        var token = _authService.GenerateToken("device-001", "sensor", publishTopics, subscribeTopics);

        // Act
        var result = _authService.ValidateToken(token);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Context!.AllowedPublish, Does.Contain("devices.device-001.data"));
        Assert.That(result.Context.AllowedPublish, Does.Contain("devices.device-001.status"));
        Assert.That(result.Context.AllowedSubscribe, Does.Contain("devices.device-001.commands"));
        Assert.That(result.Context.AllowedSubscribe, Does.Contain("devices.*.broadcasts"));
    }

    [Test]
    public void CanPublish_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { "devices.device-001.data" },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act
        var canPublish = _authService.CanPublish(result.Context!, "devices.device-001.data");

        // Assert
        Assert.That(canPublish, Is.True);
    }

    [Test]
    public void CanPublish_WithNoMatch_ReturnsFalse()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { "devices.device-001.data" },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act
        var canPublish = _authService.CanPublish(result.Context!, "devices.device-002.data");

        // Assert
        Assert.That(canPublish, Is.False);
    }

    [Test]
    public void CanPublish_WithWildcard_MatchesSingleToken()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { "devices.*.data" },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act & Assert
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-001.data"), Is.True);
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-002.data"), Is.True);
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-001.status"), Is.False);
    }

    [Test]
    public void CanPublish_WithGreaterThan_MatchesMultipleTokens()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { "devices.>" },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act & Assert
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-001"), Is.True);
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-001.data"), Is.True);
        Assert.That(_authService.CanPublish(result.Context!, "devices.sensor-001.data.temperature"), Is.True);
        Assert.That(_authService.CanPublish(result.Context!, "other.sensor-001"), Is.False);
    }

    [Test]
    public void CanSubscribe_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            Array.Empty<string>(),
            new[] { "devices.device-001.commands" });
        var result = _authService.ValidateToken(token);

        // Act
        var canSubscribe = _authService.CanSubscribe(result.Context!, "devices.device-001.commands");

        // Assert
        Assert.That(canSubscribe, Is.True);
    }

    [Test]
    public void CanSubscribe_WithWildcard_MatchesSingleToken()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            Array.Empty<string>(),
            new[] { "devices.*.commands" });
        var result = _authService.ValidateToken(token);

        // Act & Assert
        Assert.That(_authService.CanSubscribe(result.Context!, "devices.sensor-001.commands"), Is.True);
        Assert.That(_authService.CanSubscribe(result.Context!, "devices.actuator-001.commands"), Is.True);
        Assert.That(_authService.CanSubscribe(result.Context!, "devices.sensor-001.data"), Is.False);
    }

    [Test]
    public void CanPublish_WithNullContext_ReturnsFalse()
    {
        // Act
        var result = _authService.CanPublish(null!, "some.subject");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanPublish_WithEmptySubject_ReturnsFalse()
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { "devices.>" },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act
        var canPublish = _authService.CanPublish(result.Context!, "");

        // Assert
        Assert.That(canPublish, Is.False);
    }

    [TestCase("devices.sensor-001.data", "devices.sensor-001.data", true)]
    [TestCase("devices.*.data", "devices.sensor-001.data", true)]
    [TestCase("devices.>", "devices.sensor-001.data", true)]
    [TestCase("devices.sensor-001.>", "devices.sensor-001.data.temp", true)]
    [TestCase("devices.sensor-001.data", "devices.sensor-002.data", false)]
    [TestCase("devices.*.data", "devices.sensor-001.status", false)]
    [TestCase("devices.sensor-001", "devices.sensor-001.data", false)]
    public void CanPublish_VariousPatterns(string pattern, string subject, bool expected)
    {
        // Arrange
        var token = _authService.GenerateToken(
            "device-001", "sensor",
            new[] { pattern },
            Array.Empty<string>());
        var result = _authService.ValidateToken(token);

        // Act
        var canPublish = _authService.CanPublish(result.Context!, subject);

        // Assert
        Assert.That(canPublish, Is.EqualTo(expected));
    }

    [Test]
    public void GenerateToken_CreatesValidToken()
    {
        // Act
        var token = _authService.GenerateToken(
            "device-001",
            "admin",
            new[] { "devices.>" },
            new[] { "devices.>" });

        // Assert
        Assert.That(token, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Split('.').Length, Is.EqualTo(3)); // JWT has 3 parts
    }
}
