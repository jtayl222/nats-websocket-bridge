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
    private readonly IDeviceAuthenticationService _authService;
    private readonly IDeviceAuthorizationService _authzService;
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
        IDeviceAuthenticationService authService,
        IDeviceAuthorizationService authzService,
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
        _authzService = authzService;
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
    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken)
    {
        string? deviceId = null;
        string deviceType = "unknown";
        var connectionStartTime = Stopwatch.GetTimestamp();
        var subscriptionIds = new ConcurrentDictionary<string, string>();
        var disconnectReason = "normal";

        using var activity = GatewayMetrics.ActivitySource.StartActivity("HandleDeviceConnection");

        try
        {
            // Wait for authentication
            var device = await AuthenticateConnectionAsync(webSocket, cancellationToken);
            if (device == null)
            {
                disconnectReason = "auth_failed";
                await CloseWithErrorAsync(webSocket, "Authentication failed", cancellationToken);
                return;
            }

            deviceId = device.DeviceId;
            deviceType = device.DeviceType ?? "unknown";
            activity?.SetTag("device.id", deviceId);
            activity?.SetTag("device.type", deviceType);

            _connectionManager.RegisterConnection(deviceId, device, webSocket);
            _bufferService.CreateBuffer(deviceId);
            _metrics.ConnectionOpened(deviceType);

            _logger.LogInformation("Device {DeviceId} of type {DeviceType} connected",
                deviceId, deviceType);

            // Start background tasks
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sendTask = SendBufferedMessagesAsync(deviceId, webSocket, cts.Token);
            var receiveTask = ReceiveMessagesAsync(deviceId, device, webSocket, subscriptionIds, cts.Token);

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
            _logger.LogWarning(ex, "WebSocket error for device {DeviceId}", deviceId ?? "unknown");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        catch (Exception ex)
        {
            disconnectReason = "error";
            _logger.LogError(ex, "Error handling device {DeviceId}", deviceId ?? "unknown");
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

            if (deviceId != null)
            {
                _connectionManager.RemoveConnection(deviceId);
                _bufferService.RemoveBuffer(deviceId);
                _throttlingService.Reset(deviceId);

                // Record connection metrics
                var connectionDuration = Stopwatch.GetElapsedTime(connectionStartTime).TotalSeconds;
                _metrics.ConnectionClosed(deviceType, disconnectReason);
                _metrics.RecordConnectionDuration(deviceType, connectionDuration);
            }

            _logger.LogInformation("Device {DeviceId} disconnected after {Duration:F2}s. Reason: {Reason}",
                deviceId ?? "unknown",
                Stopwatch.GetElapsedTime(connectionStartTime).TotalSeconds,
                disconnectReason);
        }
    }
    
    private async Task<DeviceInfo?> AuthenticateConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken)
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

            var authRequest = JsonSerializer.Deserialize<AuthenticationRequest>(
                message.Payload.ToString() ?? "{}", _jsonOptions);

            if (authRequest == null)
            {
                _logger.LogWarning("Failed to parse authentication request");
                _metrics.AuthAttempt("failure");
                return null;
            }

            var authResponse = await _authService.AuthenticateAsync(authRequest, authCts.Token);

            // Send auth response
            var responseMessage = new GatewayMessage
            {
                Type = MessageType.Auth,
                Payload = authResponse
            };

            var responseJson = JsonSerializer.Serialize(responseMessage, _jsonOptions);
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await webSocket.SendAsync(responseBytes, WebSocketMessageType.Text, true, cancellationToken);

            _metrics.RecordMessageSize("sent", responseBytes.Length);
            _metrics.RecordAuthDuration(Stopwatch.GetElapsedTime(authStart).TotalSeconds);

            if (authResponse.Device != null)
            {
                _metrics.AuthAttempt("success");
                _logger.LogInformation("Device {DeviceId} authenticated successfully", authRequest.DeviceId);
            }
            else
            {
                _metrics.AuthAttempt("failure");
                _logger.LogWarning("Authentication failed for device {DeviceId}", authRequest.DeviceId);
            }

            return authResponse.Device;
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
        string deviceId,
        DeviceInfo device,
        System.Net.WebSockets.WebSocket webSocket,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[_options.MaxMessageSize];

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Device {DeviceId} requested close", deviceId);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                _metrics.RecordMessageSize("received", result.Count);
                _connectionManager.UpdateLastActivity(deviceId);

                // Rate limiting
                if (!_throttlingService.TryAcquire(deviceId))
                {
                    _metrics.RateLimitRejection(deviceId);
                    _metrics.MessageSent("error", deviceId);
                    await SendErrorAsync(webSocket, "Rate limit exceeded", cancellationToken);
                    continue;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<GatewayMessage>(json, _jsonOptions);

                if (message == null)
                {
                    _metrics.MessageSent("error", deviceId);
                    await SendErrorAsync(webSocket, "Invalid message format", cancellationToken);
                    continue;
                }

                _metrics.MessageReceived(message.Type.ToString().ToLowerInvariant(), deviceId);

                // Validate message
                var validationResult = _validationService.Validate(message);
                if (!validationResult.IsValid)
                {
                    _metrics.MessageSent("error", deviceId);
                    await SendErrorAsync(webSocket, validationResult.ErrorMessage!, cancellationToken);
                    continue;
                }

                // Handle message with timing
                var processStart = Stopwatch.GetTimestamp();
                await HandleMessageAsync(deviceId, device, message, subscriptionIds, cancellationToken);
                _metrics.RecordMessageProcessingDuration(
                    message.Type.ToString().ToLowerInvariant(),
                    Stopwatch.GetElapsedTime(processStart).TotalSeconds);
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogInformation("Device {DeviceId} closed connection", deviceId);
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving message from device {DeviceId}", deviceId);
            }
        }
    }
    
    private async Task HandleMessageAsync(
        string deviceId,
        DeviceInfo device,
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case MessageType.Publish:
                await HandlePublishAsync(deviceId, device, message, cancellationToken);
                break;
                
            case MessageType.Subscribe:
                await HandleSubscribeAsync(deviceId, device, message, subscriptionIds, cancellationToken);
                break;
                
            case MessageType.Unsubscribe:
                await HandleUnsubscribeAsync(message, subscriptionIds, cancellationToken);
                break;
                
            case MessageType.Ping:
                await HandlePingAsync(deviceId, cancellationToken);
                break;
                
            default:
                _logger.LogWarning("Unhandled message type {Type} from device {DeviceId}", message.Type, deviceId);
                break;
        }
    }
    
    private async Task HandlePublishAsync(string deviceId, DeviceInfo device, GatewayMessage message, CancellationToken cancellationToken)
    {
        // Check authorization
        var canPublish = _authzService.CanPublish(device, message.Subject);
        _metrics.AuthorizationCheck("publish", canPublish);

        if (!canPublish)
        {
            _logger.LogWarning("Device {DeviceId} not authorized to publish to {Subject}", deviceId, message.Subject);
            var ws = _connectionManager.GetConnection(deviceId);
            if (ws != null)
            {
                _metrics.MessageSent("error", deviceId);
                await SendErrorAsync(ws, $"Not authorized to publish to {message.Subject}", cancellationToken);
            }
            return;
        }

        // Add device ID to message
        message.DeviceId = deviceId;
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
            _logger.LogDebug("Device {DeviceId} published to {Subject} (seq: {Sequence})", deviceId, message.Subject, result.Sequence);
        }
        catch (Exception ex)
        {
            _metrics.NatsPublishError("jetstream");
            _metrics.RecordNatsLatency("publish", Stopwatch.GetElapsedTime(natsStart).TotalSeconds);
            _logger.LogError(ex, "Failed to publish message from {DeviceId} to {Subject}", deviceId, message.Subject);
            throw;
        }
    }
    
    private async Task HandleSubscribeAsync(
        string deviceId,
        DeviceInfo device,
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        // Check authorization
        var canSubscribe = _authzService.CanSubscribe(device, message.Subject);
        _metrics.AuthorizationCheck("subscribe", canSubscribe);

        if (!canSubscribe)
        {
            _logger.LogWarning("Device {DeviceId} not authorized to subscribe to {Subject}", deviceId, message.Subject);
            var ws = _connectionManager.GetConnection(deviceId);
            if (ws != null)
            {
                _metrics.MessageSent("error", deviceId);
                await SendErrorAsync(ws, $"Not authorized to subscribe to {message.Subject}", cancellationToken);
            }
            return;
        }

        // Subscribe to NATS subject using JetStream
        var natsStart = Stopwatch.GetTimestamp();
        var subscription = await _jetStreamService.SubscribeDeviceAsync(
            deviceId,
            message.Subject,
            async (msg) =>
            {
                // Forward message to device
                var incomingMessage = new GatewayMessage
                {
                    Type = MessageType.Message,
                    Subject = msg.Subject,
                    Payload = JsonSerializer.Deserialize<object>(msg.Data, _jsonOptions),
                    Timestamp = msg.Timestamp
                };

                _metrics.MessageSent("message", deviceId);
                _bufferService.Enqueue(deviceId, incomingMessage);
                
                // Acknowledge the message
                await _jetStreamService.AckMessageAsync(msg);
            },
            cancellationToken: cancellationToken);

        _metrics.NatsSubscribe();
        _metrics.RecordNatsLatency("subscribe", Stopwatch.GetElapsedTime(natsStart).TotalSeconds);

        subscriptionIds[message.Subject] = subscription.SubscriptionId;
        _logger.LogInformation("Device {DeviceId} subscribed to {Subject} (consumer: {Consumer})", deviceId, message.Subject, subscription.ConsumerName);

        // Send ack
        var ackMessage = new GatewayMessage
        {
            Type = MessageType.Ack,
            Subject = message.Subject,
            CorrelationId = message.CorrelationId
        };
        _metrics.MessageSent("ack", deviceId);
        _bufferService.Enqueue(deviceId, ackMessage);
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
    
    private Task HandlePingAsync(string deviceId, CancellationToken cancellationToken)
    {
        var pongMessage = new GatewayMessage
        {
            Type = MessageType.Pong,
            Timestamp = DateTime.UtcNow
        };

        _metrics.MessageSent("pong", deviceId);
        _bufferService.Enqueue(deviceId, pongMessage);
        return Task.CompletedTask;
    }
    
    private async Task SendBufferedMessagesAsync(string deviceId, System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken)
    {
        var reader = _bufferService.GetReader(deviceId);
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
    
    private async Task SendErrorAsync(System.Net.WebSockets.WebSocket webSocket, string error, CancellationToken cancellationToken)
    {
        var errorMessage = new GatewayMessage
        {
            Type = MessageType.Error,
            Payload = new { error }
        };
        
        var json = JsonSerializer.Serialize(errorMessage, _jsonOptions);
        var data = Encoding.UTF8.GetBytes(json);
        
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.SendAsync(data, WebSocketMessageType.Text, true, cancellationToken);
        }
    }
    
    private async Task CloseWithErrorAsync(System.Net.WebSockets.WebSocket webSocket, string error, CancellationToken cancellationToken)
    {
        await SendErrorAsync(webSocket, error, cancellationToken);
        
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, error, cancellationToken);
        }
    }
}
