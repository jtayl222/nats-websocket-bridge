using System.Collections.Concurrent;
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
    private readonly INatsService _natsService;
    private readonly IMessageValidationService _validationService;
    private readonly IMessageThrottlingService _throttlingService;
    private readonly IMessageBufferService _bufferService;
    private readonly GatewayOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public DeviceWebSocketHandler(
        ILogger<DeviceWebSocketHandler> logger,
        IDeviceAuthenticationService authService,
        IDeviceAuthorizationService authzService,
        IDeviceConnectionManager connectionManager,
        INatsService natsService,
        IMessageValidationService validationService,
        IMessageThrottlingService throttlingService,
        IMessageBufferService bufferService,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _authService = authService;
        _authzService = authzService;
        _connectionManager = connectionManager;
        _natsService = natsService;
        _validationService = validationService;
        _throttlingService = throttlingService;
        _bufferService = bufferService;
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
        var subscriptionIds = new ConcurrentDictionary<string, string>();
        
        try
        {
            // Wait for authentication
            var device = await AuthenticateConnectionAsync(webSocket, cancellationToken);
            if (device == null)
            {
                await CloseWithErrorAsync(webSocket, "Authentication failed", cancellationToken);
                return;
            }
            
            deviceId = device.DeviceId;
            _connectionManager.RegisterConnection(deviceId, device, webSocket);
            _bufferService.CreateBuffer(deviceId);
            
            _logger.LogInformation("Device {DeviceId} connected", deviceId);
            
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
            _logger.LogWarning(ex, "WebSocket error for device {DeviceId}", deviceId ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling device {DeviceId}", deviceId ?? "unknown");
        }
        finally
        {
            // Cleanup subscriptions
            foreach (var subId in subscriptionIds.Values)
            {
                try
                {
                    await _natsService.UnsubscribeAsync(subId);
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
            }
            
            _logger.LogInformation("Device {DeviceId} disconnected", deviceId ?? "unknown");
        }
    }
    
    private async Task<DeviceInfo?> AuthenticateConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken)
    {
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authCts.CancelAfter(TimeSpan.FromSeconds(_options.AuthenticationTimeoutSeconds));
        
        try
        {
            var buffer = new byte[4096];
            var result = await webSocket.ReceiveAsync(buffer, authCts.Token);
            
            if (result.MessageType != WebSocketMessageType.Text)
            {
                _logger.LogWarning("Expected text message for authentication");
                return null;
            }
            
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var message = JsonSerializer.Deserialize<GatewayMessage>(json, _jsonOptions);
            
            if (message?.Type != MessageType.Auth || message.Payload == null)
            {
                _logger.LogWarning("Invalid authentication message");
                return null;
            }
            
            var authRequest = JsonSerializer.Deserialize<AuthenticationRequest>(
                message.Payload.ToString() ?? "{}", _jsonOptions);
                
            if (authRequest == null)
            {
                _logger.LogWarning("Failed to parse authentication request");
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
            
            return authResponse.Device;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Authentication timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication");
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
                
                _connectionManager.UpdateLastActivity(deviceId);
                
                // Rate limiting
                if (!_throttlingService.TryAcquire(deviceId))
                {
                    await SendErrorAsync(webSocket, "Rate limit exceeded", cancellationToken);
                    continue;
                }
                
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<GatewayMessage>(json, _jsonOptions);
                
                if (message == null)
                {
                    await SendErrorAsync(webSocket, "Invalid message format", cancellationToken);
                    continue;
                }
                
                // Validate message
                var validationResult = _validationService.Validate(message);
                if (!validationResult.IsValid)
                {
                    await SendErrorAsync(webSocket, validationResult.ErrorMessage!, cancellationToken);
                    continue;
                }
                
                // Handle message
                await HandleMessageAsync(deviceId, device, message, subscriptionIds, cancellationToken);
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
        if (!_authzService.CanPublish(device, message.Subject))
        {
            _logger.LogWarning("Device {DeviceId} not authorized to publish to {Subject}", deviceId, message.Subject);
            var ws = _connectionManager.GetConnection(deviceId);
            if (ws != null)
            {
                await SendErrorAsync(ws, $"Not authorized to publish to {message.Subject}", cancellationToken);
            }
            return;
        }
        
        // Add device ID to message
        message.DeviceId = deviceId;
        message.Timestamp = DateTime.UtcNow;
        
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var data = Encoding.UTF8.GetBytes(json);
        
        await _natsService.PublishToJetStreamAsync(message.Subject, data, cancellationToken);
        _logger.LogDebug("Device {DeviceId} published to {Subject}", deviceId, message.Subject);
    }
    
    private async Task HandleSubscribeAsync(
        string deviceId,
        DeviceInfo device,
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        // Check authorization
        if (!_authzService.CanSubscribe(device, message.Subject))
        {
            _logger.LogWarning("Device {DeviceId} not authorized to subscribe to {Subject}", deviceId, message.Subject);
            var ws = _connectionManager.GetConnection(deviceId);
            if (ws != null)
            {
                await SendErrorAsync(ws, $"Not authorized to subscribe to {message.Subject}", cancellationToken);
            }
            return;
        }
        
        // Subscribe to NATS subject
        var subscriptionId = await _natsService.SubscribeAsync(
            message.Subject,
            async (subject, data) =>
            {
                // Forward message to device
                var incomingMessage = new GatewayMessage
                {
                    Type = MessageType.Message,
                    Subject = subject,
                    Payload = JsonSerializer.Deserialize<object>(data, _jsonOptions),
                    Timestamp = DateTime.UtcNow
                };
                
                _bufferService.Enqueue(deviceId, incomingMessage);
            },
            cancellationToken);
            
        subscriptionIds[message.Subject] = subscriptionId;
        _logger.LogInformation("Device {DeviceId} subscribed to {Subject}", deviceId, message.Subject);
        
        // Send ack
        var ackMessage = new GatewayMessage
        {
            Type = MessageType.Ack,
            Subject = message.Subject,
            CorrelationId = message.CorrelationId
        };
        _bufferService.Enqueue(deviceId, ackMessage);
    }
    
    private async Task HandleUnsubscribeAsync(
        GatewayMessage message,
        ConcurrentDictionary<string, string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        if (subscriptionIds.TryRemove(message.Subject, out var subscriptionId))
        {
            await _natsService.UnsubscribeAsync(subscriptionId, cancellationToken);
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
