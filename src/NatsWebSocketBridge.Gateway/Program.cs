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
builder.Services.Configure<JetStreamOptions>(
    builder.Configuration.GetSection(JetStreamOptions.SectionName));

// Add authentication and authorization services
builder.Services.AddSingleton<IDeviceAuthenticationService, InMemoryDeviceAuthenticationService>();
builder.Services.AddSingleton<IDeviceAuthorizationService, DeviceAuthorizationService>();

// Add core services
builder.Services.AddSingleton<IDeviceConnectionManager, DeviceConnectionManager>();
builder.Services.AddSingleton<IMessageValidationService, MessageValidationService>();
builder.Services.AddSingleton<IMessageThrottlingService, TokenBucketThrottlingService>();
builder.Services.AddSingleton<IMessageBufferService, MessageBufferService>();

// Add JetStream NATS service (recommended)
builder.Services.AddSingleton<IJetStreamNatsService, JetStreamNatsService>();

// Keep legacy service for backward compatibility (deprecated)
#pragma warning disable CS0618 // Type or member is obsolete
builder.Services.AddSingleton<INatsService, NatsService>();
#pragma warning restore CS0618

// Add WebSocket handler
builder.Services.AddScoped<DeviceWebSocketHandler>();

// Add hosted service for NATS initialization
builder.Services.AddHostedService<NatsInitializationService>();

// Add JetStream initialization service
builder.Services.AddHostedService<JetStreamInitializationService>();

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
app.MapGet("/health", (IDeviceConnectionManager connectionManager, IJetStreamNatsService jetStreamService) =>
{
    return Results.Ok(new
    {
        status = "healthy",
        connectedDevices = connectionManager.ConnectionCount,
        natsConnected = jetStreamService.IsConnected,
        jetStreamAvailable = jetStreamService.IsJetStreamAvailable
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

// JetStream stream info endpoint
app.MapGet("/jetstream/streams", async (IJetStreamNatsService jetStreamService, CancellationToken cancellationToken) =>
{
    var streams = await jetStreamService.GetAllStreamsAsync(cancellationToken);
    return Results.Ok(streams);
}).WithName("GetJetStreamStreams");

// JetStream consumers endpoint
app.MapGet("/jetstream/streams/{streamName}/consumers", async (string streamName, IJetStreamNatsService jetStreamService, CancellationToken cancellationToken) =>
{
    var consumers = await jetStreamService.GetAllConsumersAsync(streamName, cancellationToken);
    return Results.Ok(consumers);
}).WithName("GetJetStreamConsumers");

app.Run();
