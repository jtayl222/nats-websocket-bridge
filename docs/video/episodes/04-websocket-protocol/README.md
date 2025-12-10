# Episode 04: WebSocket Protocol

**Duration:** 10-12 minutes
**Prerequisites:** [Episode 01](../01-intro/README.md), [Episode 02](../02-nats-fundamentals/README.md), [Episode 03](../03-gateway-architecture/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (4 of 7)

> **Hands-on Guide:** For a comprehensive developer tutorial with state diagrams, sequence diagrams, and unit testing patterns, see [WebSocket Developer Tutorial](WS_DEVELOPER_TUTORIAL.md).

## Learning Objectives

By the end of this episode, viewers will understand:
- WebSocket authentication flow with JWT tokens
- Message format design (JSON envelope with numeric type codes)
- Publish, subscribe, and request patterns for telemetry and commands
- Error handling and reconnection strategies for factory floor reliability

## Outline

1. **Protocol Overview** (0:00-1:30)
   - Why define a custom protocol
   - Message envelope structure
   - Type field for routing

2. **Authentication Flow** (1:30-4:00)
   - Connection establishment
   - AUTH message with JWT token
   - AUTH_RESPONSE success/failure
   - JWT claims: clientId, role, permissions
   - Demo: Authentication sequence

3. **Message Types** (4:00-7:00)
   - PUBLISH: Device → Cloud telemetry
   - SUBSCRIBE: Cloud → Device commands
   - REQUEST/RESPONSE: Synchronous calls
   - PING/PONG: Keep-alive
   - ERROR: Protocol errors

4. **Message Format Deep Dive** (7:00-9:00)
   - JSON structure
   - Required vs optional fields
   - Headers for tracing
   - Binary payload support

5. **Error Handling** (9:00-10:30)
   - Error codes and messages
   - Graceful disconnection
   - Reconnection strategy
   - Demo: Error scenarios

6. **Wrap-up** (10:30-11:00)
   - Protocol documentation
   - Preview: Device SDK

## Message Format Examples

The protocol uses numeric message types for efficiency:

| Type | Code | Direction | Description |
|------|------|-----------|-------------|
| Publish | 0 | Client → Server | Send telemetry to NATS |
| Subscribe | 1 | Client → Server | Subscribe to subjects |
| Unsubscribe | 2 | Client → Server | Unsubscribe from subjects |
| Message | 3 | Server → Client | Incoming subscribed message |
| Request | 4 | Client → Server | Request/reply pattern |
| Reply | 5 | Server → Client | Response to request |
| Ack | 6 | Server → Client | Subscription confirmation |
| Error | 7 | Server → Client | Error response |
| Auth | 8 | Bidirectional | Authentication |
| Ping | 9 | Client → Server | Keep-alive |
| Pong | 10 | Server → Client | Keep-alive response |

```json
// Authentication with JWT (type 8)
{"type":8,"payload":{"token":"eyJhbGciOiJIUzI1NiIs..."}}

// Successful auth response
{"type":8,"payload":{"success":true,"clientId":"SENSOR-001","role":"sensor"}}

// Publish Telemetry (type 0)
{"type":0,"subject":"factory.line1.sensor.temp","payload":{"value":23.5,"unit":"C"},"correlationId":"abc-123"}

// Subscribe to Commands (type 1)
{"type":1,"subject":"commands.SENSOR-001.>"}

// Incoming Message (type 3)
{"type":3,"subject":"commands.SENSOR-001.calibrate","payload":{"action":"calibrate","offset":0.5}}

// Error Response (type 7)
{"type":7,"payload":{"error":"Not authorized to publish to subject"}}
```

## Demo

```bash
# Start gateway in development mode
cd src/NatsWebSocketBridge.Gateway && dotnet run

# Generate a JWT token for testing
TOKEN=$(curl -s -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"demo-device","role":"sensor","publish":["telemetry.>","factory.>"],"subscribe":["commands.demo-device.>"]}' \
  | jq -r '.token')

# Option 1: Header-based auth (recommended for CLI tools)
# Token is passed in Authorization header - no AUTH message needed
wscat -c ws://localhost:5000/ws -H "Authorization: Bearer $TOKEN"

# Option 2: In-band auth (required for browsers)
wscat -c ws://localhost:5000/ws
# Then send auth message with JWT (type 8 = Auth)
{"type":8,"payload":{"token":"<paste TOKEN here>"}}

# Publish telemetry (type 0 = Publish)
{"type":0,"subject":"telemetry.demo-device.temp","payload":{"value":23.5}}

# Subscribe to commands (type 1 = Subscribe)
{"type":1,"subject":"commands.demo-device.>"}

# Keep-alive ping (type 9 = Ping)
{"type":9}
```

**Authentication Methods:**

| Method | Use Case | How |
|--------|----------|-----|
| Header-based | CLI tools (wscat, curl) | `-H "Authorization: Bearer $TOKEN"` |
| In-band | Browsers, SDKs | Send `{"type":8,"payload":{"token":"..."}}` after connect |

**JWT Token Generation (Development only):**
```bash
# Default permissions (full access)
curl -X POST http://localhost:5000/dev/token -d '{"clientId":"my-device"}'

# Sensor with limited permissions
curl -X POST http://localhost:5000/dev/token \
  -d '{"clientId":"SENSOR-001","role":"sensor","publish":["telemetry.>"],"subscribe":["commands.SENSOR-001.>"]}'

# Custom expiry (48 hours)
curl -X POST http://localhost:5000/dev/token \
  -d '{"clientId":"test","expiryHours":48}'
```

## Key Visuals

- Authentication sequence diagram
- Message type decision tree
- JSON schema visualization
- Error code reference table

## Pharmaceutical Telemetry Examples

Typical message patterns from packaging line equipment:

```json
// Blister sealer temperature monitoring
{"type":0,"subject":"telemetry.line1.blister-sealer.temperature","payload":{"value":185.5,"unit":"C","zone":"upper","batchId":"B2024-001"}}

// Cartoner reject event
{"type":0,"subject":"events.line1.cartoner.reject","payload":{"reason":"missing_leaflet","productId":"ABC123","timestamp":"2024-01-15T10:30:00Z"}}

// Case packer weight verification
{"type":0,"subject":"quality.line1.case-packer.weight","payload":{"measured":5.234,"target":5.200,"tolerance":0.050,"status":"pass"}}

// Alert: Temperature excursion
{"type":0,"subject":"alerts.line1.blister-sealer.temperature-high","payload":{"value":195.2,"threshold":190.0,"severity":"warning"}}
```

## Related Documentation

- [WebSocket Developer Tutorial](WS_DEVELOPER_TUTORIAL.md) - Comprehensive implementation guide
- [Episode 03: Gateway Architecture](../03-gateway-architecture/README.md) - Server-side handling
- [Episode 05: Device SDK](../05-device-sdk/README.md) - C++ client implementation

## Next Episode

→ [Episode 05: Device SDK](../05-device-sdk/README.md) - Building the C++ SDK for embedded devices
