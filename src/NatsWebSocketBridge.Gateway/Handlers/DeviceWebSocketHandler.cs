using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;
using NatsWebSocketBridge.Gateway.Services;

namespace NatsWebSocketBridge.Gateway.Handlers;

/// <summary>
/// Handles WebSocket connections from devices
/// </summary>
public class DeviceWebSocketHandler
{
    private readonly ILogger<DeviceWebSocketHandler> _logger;
    private readonly IJwtDeviceAuthService _authService;
    private readonly IDeviceConnectionManager _connectionManager;
    private readonly IJetStreamNatsService _jetStreamService;
    private readonly IMessageValidationService _validationService;
    private readonly IMessageThrottlingService _throttlingService;
    private readonly IMessageBufferService _bufferService;
    private readonly IGatewayMetrics _metrics;
    private readonly GatewayOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public DeviceWebSocketHandler(
        ILogger<DeviceWebSocketHandler> logger,
        IJwtDeviceAuthService authService,
        IDeviceConnectionManager connectionManager,
        IJetStreamNatsService jetStreamService,
        IMessageValidationService validationService,
        IMessageThrottlingService throttlingService,
        IMessageBufferService bufferService,
        IGatewayMetrics metrics,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _authService = authService;
        _connectionManager = connectionManager;
        _jetStreamService = jetStreamService;
        _validationService = validationService;
        _throttlingService = throttlingService;
        _bufferService = bufferService;
        _metrics = metrics;
        _options = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Handle a WebSocket connection from a device
    /// </summary>
    public async Task HandleConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        string? clientId = null;
        string role = "unknown";
        var connectionStartTime = Stopwatch.GetTimestamp();
        var subscriptionIds = new ConcurrentDictionary<string, string>();
        var disconnectReason = "normal";

        using var activity = GatewayMetrics.ActivitySource.StartActivity("HandleDeviceConnection");

        try
        {
            // Wait for authentication (JWT token)
            var context = await AuthenticateConnectionAsync(webSocket, cancellationToken);
            if (context == null)
            {
                disconnectReason = "auth_failed";
                await CloseWithErrorAsync(webSocket, "Authentication failed", cancellationToken);
                return;
            }

            clientId = context.ClientId;
            role = context.Role;
            activity?.SetTag("device.id", clientId);
            activity?.SetTag("device.role", role);

            _connectionManager.RegisterConnection(context, webSocket);
            _bufferService.CreateBuffer(clientId);
            _metrics.ConnectionOpened(role);

            _logger.LogInformation("Device {ClientId} ({Role}) connected", clientId, role);

            // Start background tasks
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sendTask = SendBufferedMessagesAsync(clientId, webSocket, cts.Token);
            var receiveTask = ReceiveMessagesAsync(context, webSocket, subscriptionIds, cts.Token);

            // Wait for either task to complete (usually receive when connection closes)
            await Task.WhenAny(sendTask, receiveTask);
            cts.Cancel();

            try
            {
                await Task.WhenAll(sendTask, receiveTask);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation occurs
            }
        }
        catch (WebSocketException ex)
        {
            disconnectReason = "websocket_error";
            _logger.LogWarning(ex, "WebSocket error for device {ClientId}", clientId ?? "unknown");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        catch (Exception ex)
        {
            disconnectReason = "error";
            _logger.LogError(ex, "Error handling device {ClientId}", clientId ?? "unknown");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            // Cleanup subscriptions
            foreach (var subId in subscriptionIds.Values)
            {
                try
                {
                    await _jetStreamService.UnsubscribeAsync(subId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error unsubscribing {SubscriptionId}", subId);
                }
            }

            if (clientId != null)
            {
                _connectionManager.RemoveConnection(clientId);
                _bufferService.RemoveBuffer(clientId);
                _throttlingService.Reset(clientId);

                // Record connection metrics
                var connectionDuration = Stopwatch.GetElapsedTime(connectionStartTime).TotalSeconds;
                _metrics.ConnectionClosed(role, disconnectReason);
                _metrics.RecordConnectionDuration(role, connectionDuration);
            }

            _logger.LogInformation("Device {ClientId} disconnected after {Duration:F2}s. Reason: {Reason}",
                clientId ?? "unknown",
                Stopwatch.GetElapsedTime(connectionStartTime).TotalSeconds,
                disconnectReason);
        }
    }

    private async Task<DeviceContext?> AuthenticateConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var authStart = Stopwatch.GetTimestamp();
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authCts.CancelAfter(TimeSpan.FromSeconds(_options.AuthenticationTimeoutSeconds));

        try
        {
            var buffer = new byte[4096];
            var result = await webSocket.ReceiveAsync(buffer, authCts.Token);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                _logger.LogWarning("Expected text message for authentication");
                _metrics.AuthAttempt("failure");
                return null;
            }

            _metrics.RecordMessageSize("received", result.Count);

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var message = JsonSerializer.Deserialize<GatewayMessage>(json, _jsonOptions);

            if (message?.Type != MessageType.Auth || message.Payload == null)
            {
                _logger.LogWarning("Invalid authentication message");
                _metrics.AuthAttempt("failure");
                return null;
            }

            // Extract JWT token from payload
            var authPayload = JsonSerializer.Deserialize<JwtAuthRequest>(
                message.Payload.Value.GetRawText(), _jsonOptions);

            if (string.IsNullOrEmpty(authPayload?.Token))
            {
                _logger.LogWarning("Missing JWT token in auth request");
                _metrics.AuthAttempt("failure");
                return null;
            }

            // Validate JWT and extract device context
            var authResult = _authService.ValidateToken(authPayload.Token);

            // Send auth response
            var authResponse = new AuthResponse
            {
                Success = authResult.IsSuccess,
                ClientId = authResult.Context?.ClientId,
                Role = authResult.Context?.Role,
                Error = authResult.Error
            };
            var responseMessage = new GatewayMessage
            {
                Type = MessageType.Auth,
                Payload = JsonSerializer.SerializeToElement(authResponse, _jsonOptions)
            };

            var responseJson = JsonSerializer.Serialize(responseMessage, _jsonOptions);
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await webSocket.SendAsync(responseBytes, WebSocketMessageType.Text, true, cancellationToken);

            _metrics.RecordMessageSize("sent", responseBytes.Length);
            _metrics.RecordAuthDuration(Stopwatch.GetElapsedTime(authStart).TotalSeconds);

            if (authResult.IsSuccess)
            {
                _metrics.AuthAttempt("success");
                _logger.LogInformation("Device {ClientId} authenticated with role {Role}",
                    authResult.Context!.ClientId, authResult.Context.Role);
            }
            else
            {
                _metrics.AuthAttempt("failure");
                _logger.LogWarning("Authentication failed: {Error}", authResult.Error);
            }

            return authResult.Context;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Authentication timed out");
            _metrics.AuthAttempt("timeout");
            _metrics.RecordAuthDuration(Stopwatch.GetElapsedTime(authStart).TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication");
            _metrics.AuthAttempt("error");
            _metrics.RecordAuthDuration(Stopwatch.GetElapsedTime(authStart).TotalSeconds);
            return null;
        }
    }

    private async Task ReceiveMessagesAsync(
        DeviceContext context,
        WebSocket webSocket,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[_options.MaxMessageSize];
        var clientId = context.ClientId;

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Device {ClientId} requested close", clientId);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                _metrics.RecordMessageSize("received", result.Count);

                // Check token expiry
                if (context.IsExpired)
                {
                    _logger.LogWarning("Device {ClientId} token expired", clientId);
                    await SendErrorAsync(webSocket, "Token expired", cancellationToken);
                    break;
                }

                // Rate limiting
                if (!_throttlingService.TryAcquire(clientId))
                {
                    _metrics.RateLimitRejection(clientId);
                    _metrics.MessageSent("error", clientId);
                    await SendErrorAsync(webSocket, "Rate limit exceeded", cancellationToken);
                    continue;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<GatewayMessage>(json, _jsonOptions);

                if (message == null)
                {
                    _metrics.MessageSent("error", clientId);
                    await SendErrorAsync(webSocket, "Invalid message format", cancellationToken);
                    continue;
                }

                _metrics.MessageReceived(message.Type.ToString().ToLowerInvariant(), clientId);

                // Validate message
                var validationResult = _validationService.Validate(message);
                if (!validationResult.IsValid)
                {
                    _metrics.MessageSent("error", clientId);
                    await SendErrorAsync(webSocket, validationResult.ErrorMessage!, cancellationToken);
                    continue;
                }

                // Handle message with timing
                var processStart = Stopwatch.GetTimestamp();
                await HandleMessageAsync(context, message, subscriptionIds, cancellationToken);
                _metrics.RecordMessageProcessingDuration(
                    message.Type.ToString().ToLowerInvariant(),
                    Stopwatch.GetElapsedTime(processStart).TotalSeconds);
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogInformation("Device {ClientId} closed connection", clientId);
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving message from device {ClientId}", clientId);
            }
        }
    }

    private async Task HandleMessageAsync(
        DeviceContext context,
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case MessageType.Publish:
                await HandlePublishAsync(context, message, cancellationToken);
                break;

            case MessageType.Subscribe:
                await HandleSubscribeAsync(context, message, subscriptionIds, cancellationToken);
                break;

            case MessageType.Unsubscribe:
                await HandleUnsubscribeAsync(message, subscriptionIds, cancellationToken);
                break;

            case MessageType.Ping:
                await HandlePingAsync(context.ClientId, cancellationToken);
                break;

            default:
                _logger.LogWarning("Unhandled message type {Type} from device {ClientId}", message.Type, context.ClientId);
                break;
        }
    }

    private async Task HandlePublishAsync(DeviceContext context, GatewayMessage message, CancellationToken cancellationToken)
    {
        var clientId = context.ClientId;

        // Check authorization using JWT claims
        var canPublish = _authService.CanPublish(context, message.Subject);
        _metrics.AuthorizationCheck("publish", canPublish);

        if (!canPublish)
        {
            _logger.LogWarning("Device {ClientId} not authorized to publish to {Subject}", clientId, message.Subject);
            var ws = _connectionManager.GetConnection(clientId);
            if (ws != null)
            {
                _metrics.MessageSent("error", clientId);
                await SendErrorAsync(ws, $"Not authorized to publish to {message.Subject}", cancellationToken);
            }
            return;
        }

        // Add device ID to message
        message.DeviceId = clientId;
        message.Timestamp = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var data = Encoding.UTF8.GetBytes(json);

        var natsStart = Stopwatch.GetTimestamp();
        try
        {
            var result = await _jetStreamService.PublishAsync(message.Subject, data, cancellationToken: cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? "Publish failed");
            }
            _metrics.NatsPublish("jetstream");
            _metrics.RecordNatsLatency("publish", Stopwatch.GetElapsedTime(natsStart).TotalSeconds);
            _logger.LogDebug("Device {ClientId} published to {Subject} (seq: {Sequence})", clientId, message.Subject, result.Sequence);
        }
        catch (Exception ex)
        {
            _metrics.NatsPublishError("jetstream");
            _metrics.RecordNatsLatency("publish", Stopwatch.GetElapsedTime(natsStart).TotalSeconds);
            _logger.LogError(ex, "Failed to publish message from {ClientId} to {Subject}", clientId, message.Subject);
            throw;
        }
    }

    private async Task HandleSubscribeAsync(
        DeviceContext context,
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        var clientId = context.ClientId;

        // Check authorization using JWT claims
        var canSubscribe = _authService.CanSubscribe(context, message.Subject);
        _metrics.AuthorizationCheck("subscribe", canSubscribe);

        if (!canSubscribe)
        {
            _logger.LogWarning("Device {ClientId} not authorized to subscribe to {Subject}", clientId, message.Subject);
            var ws = _connectionManager.GetConnection(clientId);
            if (ws != null)
            {
                _metrics.MessageSent("error", clientId);
                await SendErrorAsync(ws, $"Not authorized to subscribe to {message.Subject}", cancellationToken);
            }
            return;
        }

        // Subscribe to NATS subject using JetStream
        var natsStart = Stopwatch.GetTimestamp();
        var subscription = await _jetStreamService.SubscribeDeviceAsync(
            clientId,
            message.Subject,
            async (msg) =>
            {
                // Forward message to device
                var incomingMessage = new GatewayMessage
                {
                    Type = MessageType.Message,
                    Subject = msg.Subject,
                    Payload = JsonSerializer.Deserialize<JsonElement>(msg.Data, _jsonOptions),
                    Timestamp = msg.Timestamp
                };

                _metrics.MessageSent("message", clientId);
                _bufferService.Enqueue(clientId, incomingMessage);

                // Acknowledge the message
                await _jetStreamService.AckMessageAsync(msg);
            },
            cancellationToken: cancellationToken);

        _metrics.NatsSubscribe();
        _metrics.RecordNatsLatency("subscribe", Stopwatch.GetElapsedTime(natsStart).TotalSeconds);

        subscriptionIds[message.Subject] = subscription.SubscriptionId;
        _logger.LogInformation("Device {ClientId} subscribed to {Subject} (consumer: {Consumer})", clientId, message.Subject, subscription.ConsumerName);

        // Send ack
        var ackMessage = new GatewayMessage
        {
            Type = MessageType.Ack,
            Subject = message.Subject,
            CorrelationId = message.CorrelationId
        };
        _metrics.MessageSent("ack", clientId);
        _bufferService.Enqueue(clientId, ackMessage);
    }

    private async Task HandleUnsubscribeAsync(
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        if (subscriptionIds.TryRemove(message.Subject, out var subscriptionId))
        {
            await _jetStreamService.UnsubscribeAsync(subscriptionId, cancellationToken: cancellationToken);
            _logger.LogDebug("Unsubscribed from {Subject}", message.Subject);
        }
    }

    private Task HandlePingAsync(string clientId, CancellationToken cancellationToken)
    {
        var pongMessage = new GatewayMessage
        {
            Type = MessageType.Pong,
            Timestamp = DateTime.UtcNow
        };

        _metrics.MessageSent("pong", clientId);
        _bufferService.Enqueue(clientId, pongMessage);
        return Task.CompletedTask;
    }

    private async Task SendBufferedMessagesAsync(string clientId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var reader = _bufferService.GetReader(clientId);
        if (reader == null)
        {
            return;
        }

        try
        {
            await foreach (var message in reader.ReadAllAsync(cancellationToken))
            {
                if (webSocket.State != WebSocketState.Open)
                {
                    break;
                }

                var json = JsonSerializer.Serialize(message, _jsonOptions);
                var data = Encoding.UTF8.GetBytes(json);

                await webSocket.SendAsync(data, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    private async Task SendErrorAsync(WebSocket webSocket, string error, CancellationToken cancellationToken)
    {
        var errorMessage = new GatewayMessage
        {
            Type = MessageType.Error,
            Payload = JsonSerializer.SerializeToElement(new { error }, _jsonOptions)
        };

        var json = JsonSerializer.Serialize(errorMessage, _jsonOptions);
        var data = Encoding.UTF8.GetBytes(json);

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.SendAsync(data, WebSocketMessageType.Text, true, cancellationToken);
        }
    }

    private async Task CloseWithErrorAsync(WebSocket webSocket, string error, CancellationToken cancellationToken)
    {
        await SendErrorAsync(webSocket, error, cancellationToken);

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, error, cancellationToken);
        }
    }
}

/// <summary>
/// JWT authentication request from device
/// </summary>
public class JwtAuthRequest
{
    public string? Token { get; set; }
}

/// <summary>
/// Authentication response to device
/// </summary>
public class AuthResponse
{
    public bool Success { get; set; }
    public string? ClientId { get; set; }
    public string? Role { get; set; }
    public string? Error { get; set; }
}
