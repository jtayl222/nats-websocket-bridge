using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NatsWebSocketBridge.Gateway.Auth;

namespace NatsWebSocketBridge.Gateway.Handlers;

/// <summary>
/// Middleware for handling WebSocket connections.
/// Supports two authentication methods:
/// 1. Header-based: Pass JWT in Authorization header during WebSocket handshake
/// 2. In-band: Send AUTH message (type 8) after connection is established
/// </summary>
public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebSocketMiddleware> _logger;

    public WebSocketMiddleware(RequestDelegate next, ILogger<WebSocketMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, DeviceWebSocketHandler handler, IJwtDeviceAuthService authService)
    {
        if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogInformation("New WebSocket connection from {RemoteIpAddress}",
                context.Connection.RemoteIpAddress);

            // Check for JWT in Authorization header (optional pre-authentication)
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            Models.DeviceContext? preAuthContext = null;

            if (!string.IsNullOrEmpty(authHeader))
            {
                var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authHeader.Substring(7)
                    : authHeader;

                var authResult = authService.ValidateToken(token);
                if (authResult.IsSuccess)
                {
                    preAuthContext = authResult.Context;
                    _logger.LogInformation("Pre-authenticated via header: {ClientId} ({Role})",
                        preAuthContext!.ClientId, preAuthContext.Role);
                }
                else
                {
                    _logger.LogWarning("Header authentication failed: {Error}", authResult.Error);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync($"Authentication failed: {authResult.Error}");
                    return;
                }
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await handler.HandleConnectionAsync(webSocket, context.RequestAborted, preAuthContext);
        }
        else
        {
            await _next(context);
        }
    }
}
