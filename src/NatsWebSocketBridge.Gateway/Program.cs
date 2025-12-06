using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Services;
using NatsWebSocketBridge.Gateway.Handlers;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<NatsOptions>(
    builder.Configuration.GetSection(NatsOptions.SectionName));

// Add authentication and authorization services
builder.Services.AddSingleton<IDeviceAuthenticationService, InMemoryDeviceAuthenticationService>();
builder.Services.AddSingleton<IDeviceAuthorizationService, DeviceAuthorizationService>();

// Add core services
builder.Services.AddSingleton<IDeviceConnectionManager, DeviceConnectionManager>();
builder.Services.AddSingleton<INatsService, NatsService>();
builder.Services.AddSingleton<IMessageValidationService, MessageValidationService>();
builder.Services.AddSingleton<IMessageThrottlingService, TokenBucketThrottlingService>();
builder.Services.AddSingleton<IMessageBufferService, MessageBufferService>();

// Add WebSocket handler
builder.Services.AddScoped<DeviceWebSocketHandler>();

// Add hosted service for NATS initialization
builder.Services.AddHostedService<NatsInitializationService>();

// Add OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Add WebSocket middleware for device connections
app.UseMiddleware<WebSocketMiddleware>();

// Health check endpoint
app.MapGet("/health", (IDeviceConnectionManager connectionManager, INatsService natsService) =>
{
    return Results.Ok(new
    {
        status = "healthy",
        connectedDevices = connectionManager.ConnectionCount,
        natsConnected = natsService.IsConnected
    });
}).WithName("HealthCheck");

// Get connected devices
app.MapGet("/devices", (IDeviceConnectionManager connectionManager) =>
{
    var devices = connectionManager.GetConnectedDevices()
        .Select(id => new
        {
            deviceId = id,
            info = connectionManager.GetDeviceInfo(id)
        })
        .ToList();
    
    return Results.Ok(devices);
}).WithName("GetConnectedDevices");

app.Run();
