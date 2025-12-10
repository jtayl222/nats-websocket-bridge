# DeviceContext

The `DeviceContext` record is the central identity and authorization object for connected devices. It is extracted from JWT tokens during authentication and flows through the entire request lifecycle.

## Structure

```csharp
public sealed record DeviceContext
{
    public required string ClientId { get; init; }                    // Device identifier (JWT "sub" claim)
    public required string Role { get; init; }                        // Device role (JWT "role" claim)
    public required IReadOnlyList<string> AllowedPublish { get; init; }   // Publish permissions (JWT "pub" claim)
    public required IReadOnlyList<string> AllowedSubscribe { get; init; } // Subscribe permissions (JWT "subscribe" claim)
    public required DateTime ExpiresAt { get; init; }                 // Token expiration time
    public DateTime ConnectedAt { get; init; }                        // Connection timestamp
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;           // Computed expiry check
}
```

## How It's Used

### 1. Authentication (`JwtDeviceAuthService.cs`)

When a device connects and sends a JWT token, the service:
- Validates the token signature and expiration
- Extracts claims into a `DeviceContext`
- Returns `DeviceAuthResult.Success(context)` or `DeviceAuthResult.Failure(error)`

```csharp
public DeviceAuthResult ValidateToken(string token)
{
    // ... validate JWT ...
    var context = ExtractDeviceContext(principal, jwtToken);
    return DeviceAuthResult.Success(context);
}
```

### 2. Authorization (`JwtDeviceAuthService.cs`)

Every publish/subscribe request is checked against the context's permission lists:

```csharp
public bool CanPublish(DeviceContext context, string subject)
{
    if (context.IsExpired) return false;
    return context.AllowedPublish.Any(pattern => MatchesSubject(pattern, subject));
}

public bool CanSubscribe(DeviceContext context, string subject)
{
    if (context.IsExpired) return false;
    return context.AllowedSubscribe.Any(pattern => MatchesSubject(pattern, subject));
}
```

Permission patterns support NATS wildcards:
- `*` matches a single token: `devices.*.data` matches `devices.sensor1.data`
- `>` matches multiple tokens: `devices.>` matches `devices.sensor1.data.temperature`

### 3. Connection Management (`DeviceConnectionManager.cs`)

The context is stored with the WebSocket connection:

```csharp
public void RegisterConnection(DeviceContext context, WebSocket webSocket)
{
    var connection = new DeviceConnection(context, webSocket);
    _connections.AddOrUpdate(context.ClientId, connection, (_, _) => connection);
}
```

### 4. Request Processing (`DeviceWebSocketHandler.cs`)

The handler uses context throughout the message lifecycle:

```csharp
private async Task HandleAuthenticatedSessionAsync(
    WebSocket webSocket,
    DeviceContext context,  // Passed to all handlers
    CancellationToken cancellationToken)
{
    // Check token expiry during long sessions
    if (context.IsExpired)
    {
        await SendErrorAsync(webSocket, "Token expired", cancellationToken);
        return;
    }

    // Authorize operations
    if (!_authService.CanPublish(context, message.Subject))
    {
        await SendErrorAsync(webSocket, "Not authorized to publish", cancellationToken);
        return;
    }
}
```

### 5. API Endpoints (`Program.cs`)

The `/devices` endpoint exposes context information:

```csharp
app.MapGet("/devices", (IDeviceConnectionManager connectionManager) =>
{
    var devices = connectionManager.GetConnectedDevices()
        .Select(id =>
        {
            var ctx = connectionManager.GetDeviceContext(id);
            return new
            {
                clientId = id,
                role = ctx?.Role,
                connectedAt = ctx?.ConnectedAt,
                expiresAt = ctx?.ExpiresAt
            };
        });
    return Results.Ok(devices);
});
```

## Why It's Beneficial

### 1. Single Source of Truth
All device identity and permissions are encapsulated in one immutable record. No need to query multiple services or databases during request processing.

### 2. Stateless Authorization
Permissions are embedded in the JWT token and cached in `DeviceContext`. Authorization checks are simple in-memory operations with no external calls.

### 3. Token Expiration Enforcement
The `IsExpired` property ensures tokens are checked not just at connection time but throughout the session. Long-lived WebSocket connections will be terminated when tokens expire.

### 4. Fine-Grained Access Control
Per-device topic permissions allow:
- Sensors to publish only to their own data topics
- Actuators to subscribe only to their command topics
- Admin devices to have broader access

### 5. Immutability
As a `record` type, `DeviceContext` is immutable after creation. This prevents accidental modification and makes the code thread-safe.

### 6. Audit Trail
The `ConnectedAt` timestamp and `Role` provide useful information for logging, monitoring, and debugging.

## JWT Token Structure

The `DeviceContext` is populated from these JWT claims:

| Claim | DeviceContext Property | Example |
|-------|----------------------|---------|
| `sub` | `ClientId` | `"sensor-temp-001"` |
| `role` | `Role` | `"sensor"` |
| `pub` | `AllowedPublish` | `["devices.sensor-temp-001.data"]` |
| `subscribe` | `AllowedSubscribe` | `["devices.sensor-temp-001.commands"]` |
| `exp` | `ExpiresAt` | Unix timestamp |

Example JWT payload:
```json
{
  "sub": "sensor-temp-001",
  "role": "sensor",
  "pub": ["devices.sensor-temp-001.data", "telemetry.>"],
  "subscribe": ["devices.sensor-temp-001.commands"],
  "iss": "nats-websocket-bridge",
  "aud": "nats-devices",
  "exp": 1702166400
}
```

## Alternatives Considered

### Just ClientId (string)
Simpler but loses per-device authorization. All devices would have the same permissions, requiring authorization to be handled elsewhere (e.g., NATS server-side).

### Role-Based Only
Authorize by role instead of per-device. Simpler but less flexible - all sensors would have identical permissions.

### Database Lookup
Store permissions in a database and look up on each request. More flexible but adds latency and external dependency to every operation.

The current JWT-based `DeviceContext` approach balances flexibility, performance, and simplicity.
