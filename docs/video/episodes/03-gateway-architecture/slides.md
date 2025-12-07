# Episode 03: Gateway Architecture - Slides

---

## Slide 1: Title

# Gateway Architecture Deep Dive
## Building the WebSocket-to-NATS Bridge

**NATS WebSocket Bridge Series - Episode 03**

---

## Slide 2: Episode Goals

### What You'll Learn

- Gateway service architecture
- Middleware pipeline design
- Concurrent connection handling
- Clean architecture patterns

---

## Slide 3: The Gateway's Role

```
[Devices] ←→ [WebSocket] ←→ [Gateway] ←→ [NATS] ←→ [Backend]
              TCP/TLS        C#/.NET       Pub/Sub    Services
              Long-lived     Stateful      Stateless  Microservices
```

**The Gateway is the translator between two worlds**

---

## Slide 4: High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Gateway Service                       │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  WebSocket  │  │   Message   │  │    NATS     │     │
│  │   Handler   │→ │   Router    │→ │   Service   │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│         ↑                                    ↓          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │    Auth     │  │  Metrics    │  │  JetStream  │     │
│  │ Middleware  │  │   Service   │  │   Service   │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 5: Project Structure

```
NatsWebSocketBridge.Gateway/
├── Program.cs                  # Entry point, DI setup
├── appsettings.json           # Configuration
├── Configuration/
│   ├── GatewayOptions.cs      # WebSocket settings
│   ├── NatsOptions.cs         # NATS connection
│   └── JetStreamOptions.cs    # Stream configuration
├── Services/
│   ├── WebSocketHandler.cs    # Connection lifecycle
│   ├── JetStreamNatsService.cs # JetStream messaging
│   ├── JetStreamInitializationService.cs # Stream setup
│   └── GatewayMetrics.cs      # Prometheus metrics
├── Middleware/
│   ├── AuthenticationMiddleware.cs
│   └── RateLimitingMiddleware.cs
└── Models/
    └── WebSocketMessage.cs    # Protocol messages
```

---

## Slide 6: The Middleware Pipeline

```
Request Flow:
┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐
│  HTTP    │ → │  Auth    │ → │  Rate    │ → │ WebSocket│
│ Upgrade  │   │  Check   │   │  Limit   │   │ Handler  │
└──────────┘   └──────────┘   └──────────┘   └──────────┘
     ↓              ↓              ↓              ↓
   401/403      429 Too Many    Accept WS     Process
   Rejected      Requests       Connection    Messages
```

---

## Slide 7: WebSocket Handler

### Core Responsibilities

1. **Accept Connections** - Upgrade HTTP to WebSocket
2. **Manage Lifecycle** - Track connected clients
3. **Route Messages** - Parse and dispatch to handlers
4. **Handle Errors** - Graceful disconnection

```csharp
public class WebSocketHandler
{
    private readonly ConcurrentDictionary<string, WebSocket> _clients;
    private readonly IJetStreamNatsService _jetStreamService;
    private readonly IGatewayMetrics _metrics;

    public async Task HandleAsync(WebSocket socket, string deviceId)
    {
        _clients.TryAdd(deviceId, socket);
        _metrics.IncrementConnections();

        try {
            await ProcessMessagesAsync(socket, deviceId);
        } finally {
            _clients.TryRemove(deviceId, out _);
            _metrics.DecrementConnections();
        }
    }
}
```

---

## Slide 8: Connection State Machine

```
     ┌──────────────────────────────────────────┐
     │                                          │
     ▼                                          │
┌─────────┐   Auth    ┌─────────┐   Timeout   │
│Connecting│ ────────→│Authen-  │ ──────────→ │
└─────────┘  Success  │ ticated │             │
     │                └─────────┘             │
     │ Auth              │                    │
     │ Failed            │ Active             │
     ▼                   ▼                    │
┌─────────┐         ┌─────────┐              │
│ Closed  │ ←────── │  Active │ ─────────────┘
└─────────┘  Error  └─────────┘   Disconnect
             or         │
            Close       │ Idle Timeout
                        ▼
                   ┌─────────┐
                   │  Idle   │
                   └─────────┘
```

---

## Slide 9: Message Router

### Type-Based Dispatch

```csharp
private async Task RouteMessageAsync(WebSocketMessage message, string deviceId)
{
    switch (message.Type)
    {
        case "AUTH":
            await HandleAuthAsync(message, deviceId);
            break;
        case "PUBLISH":
            await HandlePublishAsync(message, deviceId);
            break;
        case "SUBSCRIBE":
            await HandleSubscribeAsync(message, deviceId);
            break;
        case "REQUEST":
            await HandleRequestAsync(message, deviceId);
            break;
        case "PING":
            await HandlePingAsync(deviceId);
            break;
        default:
            await SendErrorAsync(deviceId, 400, "Unknown message type");
            break;
    }
}
```

---

## Slide 10: JetStream Service Layer

### Interface Design

```csharp
public interface IJetStreamNatsService : IAsyncDisposable
{
    bool IsConnected { get; }
    bool IsJetStreamAvailable { get; }
    
    Task InitializeAsync(CancellationToken ct = default);
    Task<JetStreamPublishResult> PublishAsync(string subject, byte[] data,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken ct = default);
    Task<JetStreamSubscription> SubscribeDeviceAsync(string deviceId,
        string subject, Func<JetStreamMessage, Task> handler,
        ReplayOptions? replayOptions = null,
        CancellationToken ct = default);
    Task UnsubscribeAsync(string subscriptionId, bool deleteConsumer = false,
        CancellationToken ct = default);
    Task AckMessageAsync(JetStreamMessage message, CancellationToken ct = default);
}
```

**Why JetStream?**
- Guaranteed delivery with acknowledgements
- Message replay for reconnecting devices
- Durable consumers for reliable subscriptions

---

## Slide 11: JetStream Service Implementation

```csharp
public class JetStreamNatsService : IJetStreamNatsService
{
    private readonly NatsConnection _connection;
    private readonly NatsJSContext _jetStream;
    private readonly ILogger<JetStreamNatsService> _logger;
    private readonly IGatewayMetrics _metrics;

    public async Task<JetStreamPublishResult> PublishAsync(
        string subject, byte[] data,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var ack = await _jetStream.PublishAsync(subject, data,
                opts: new NatsJSPubOpts { MsgId = messageId }, ct);

            _metrics.RecordPublish(subject, stopwatch.ElapsedMilliseconds);
            return new JetStreamPublishResult
            {
                Success = true,
                Stream = ack.Stream,
                Sequence = ack.Seq,
                Duplicate = ack.Duplicate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish failed: {Subject}", subject);
            _metrics.IncrementPublishErrors();
            return new JetStreamPublishResult { Success = false, Error = ex.Message };
        }
    }
}
```

---

## Slide 12: JetStream Features

### Why JetStream?

| Feature | Core NATS | JetStream |
|---------|-----------|----------|
| Delivery | At-most-once | At-least-once |
| Persistence | None | Disk/Memory |
| Replay | No | Yes |
| Consumer Tracking | No | Yes |

```csharp
// Subscribe with replay for reconnecting devices
var subscription = await _jetStreamService.SubscribeDeviceAsync(
    deviceId,
    "telemetry.>",
    async (msg) =>
    {
        await ProcessMessageAsync(msg);
        await _jetStreamService.AckMessageAsync(msg);
    },
    new ReplayOptions { FromSequence = lastSeenSequence }
);
```

---

## Slide 13: Configuration System

### Layered Configuration

```csharp
// appsettings.json
{
  "Gateway": {
    "WebSocket": {
      "MaxConnections": 10000,
      "ReceiveBufferSize": 4096,
      "IdleTimeoutSeconds": 300
    }
  },
  "Nats": {
    "Url": "nats://localhost:4222",
    "MaxReconnectAttempts": -1,
    "ReconnectWaitMs": 2000
  },
  "JetStream": {
    "Enabled": true,
    "Streams": [
      {
        "Name": "TELEMETRY",
        "Subjects": ["telemetry.>"],
        "Retention": "limits",
        "MaxAge": "7d"
      }
    ]
  }
}
```

---

## Slide 14: Dependency Injection Setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection("Gateway"));
builder.Services.Configure<NatsOptions>(
    builder.Configuration.GetSection("Nats"));
builder.Services.Configure<JetStreamOptions>(
    builder.Configuration.GetSection("JetStream"));

// Services - JetStream for all NATS operations
builder.Services.AddSingleton<IJetStreamNatsService, JetStreamNatsService>();
builder.Services.AddSingleton<IGatewayMetrics, GatewayMetrics>();
builder.Services.AddSingleton<WebSocketHandler>();

// Hosted Services
builder.Services.AddHostedService<JetStreamInitializationService>();

var app = builder.Build();

// WebSocket middleware
app.UseWebSockets();
app.Map("/ws", async context => {
    if (context.WebSockets.IsWebSocketRequest) {
        var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        await handler.HandleAsync(context);
    }
});
```

---

## Slide 15: Concurrency Patterns

### Managing 10,000 Connections

```csharp
// Thread-safe client tracking
private readonly ConcurrentDictionary<string, ClientContext> _clients = new();

// Async enumeration for subscriptions
public async IAsyncEnumerable<NatsMessage> SubscribeAsync(
    string subject,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, ct))
    {
        yield return new NatsMessage(msg.Subject, msg.Data, msg.Headers);
    }
}

// Semaphore for resource limiting
private readonly SemaphoreSlim _publishSemaphore = new(100);

public async Task<bool> PublishWithBackpressureAsync(...)
{
    await _publishSemaphore.WaitAsync(ct);
    try {
        return await PublishAsync(...);
    } finally {
        _publishSemaphore.Release();
    }
}
```

---

## Slide 16: Error Handling Strategy

### Graceful Degradation

```csharp
public async Task HandleAsync(HttpContext context)
{
    WebSocket? socket = null;
    string? deviceId = null;

    try
    {
        socket = await context.WebSockets.AcceptWebSocketAsync();
        deviceId = await AuthenticateAsync(socket);

        await ProcessMessagesAsync(socket, deviceId);
    }
    catch (WebSocketException ex)
    {
        _logger.LogWarning(ex, "WebSocket error: {DeviceId}", deviceId);
        // Connection already closed, just cleanup
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Connection cancelled: {DeviceId}", deviceId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error: {DeviceId}", deviceId);
        await SendErrorAsync(socket, 500, "Internal server error");
    }
    finally
    {
        await CleanupAsync(deviceId, socket);
    }
}
```

---

## Slide 17: Metrics Integration

### What We Measure

```csharp
public interface IGatewayMetrics
{
    // Connections
    void IncrementConnections();
    void DecrementConnections();
    void RecordConnectionDuration(double seconds);

    // Messages
    void IncrementMessagesReceived(string messageType);
    void IncrementMessagesSent(string messageType);
    void RecordMessageSize(int bytes);

    // NATS
    void RecordPublishLatency(double milliseconds);
    void IncrementPublishErrors();

    // Auth
    void IncrementAuthSuccess();
    void IncrementAuthFailure(string reason);
}
```

---

## Slide 18: Demo Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Demo Environment                      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│   ┌─────────┐    ┌─────────────┐    ┌─────────┐        │
│   │  wscat  │───→│   Gateway   │───→│  NATS   │        │
│   │ (client)│    │  :5000/ws   │    │  :4222  │        │
│   └─────────┘    └─────────────┘    └─────────┘        │
│                         │                 │             │
│                         ▼                 ▼             │
│                  ┌─────────────┐   ┌─────────────┐     │
│                  │  Prometheus │   │  nats sub   │     │
│                  │   :9090     │   │  (monitor)  │     │
│                  └─────────────┘   └─────────────┘     │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 19: Key Takeaways

### Gateway Architecture Principles

1. **Separation of Concerns** - Each service has one job
2. **Interface-First Design** - Enables testing and flexibility
3. **Configuration-Driven** - Behavior changes without code
4. **Graceful Degradation** - Handle failures without crashing
5. **Observable by Default** - Metrics everywhere

---

## Slide 20: Next Episode Preview

# Episode 04: WebSocket Protocol

- Authentication flow design
- Message format specification
- Error handling patterns
- Protocol documentation

**See you in the next episode!**
