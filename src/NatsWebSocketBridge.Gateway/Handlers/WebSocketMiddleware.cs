using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NatsWebSocketBridge.Gateway.Handlers;

/// <summary>
/// Middleware for handling WebSocket connections
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
    
    public async Task InvokeAsync(HttpContext context, DeviceWebSocketHandler handler)
    {
        if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogInformation("New WebSocket connection from {RemoteIpAddress}", 
                context.Connection.RemoteIpAddress);
            
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await handler.HandleConnectionAsync(webSocket, context.RequestAborted);
        }
        else
        {
            await _next(context);
        }
    }
}
