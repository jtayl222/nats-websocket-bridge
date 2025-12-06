# Episode 04: WebSocket Protocol - Slides

---

## Slide 1: Title

# WebSocket Protocol Design
## Defining the Device-Gateway Contract

**NATS WebSocket Bridge Series - Episode 04**

---

## Slide 2: Episode Goals

### What You'll Learn

- Protocol envelope design
- Authentication flow
- Message types and formats
- Error handling patterns

---

## Slide 3: Why a Custom Protocol?

### Raw WebSocket is Just Bytes

- No built-in message types
- No authentication standard
- No error format
- No correlation for request/response

### Our Protocol Provides

- **Type field** - Route messages correctly
- **Structured format** - Consistent JSON envelope
- **Auth flow** - Secure device identity
- **Error codes** - Machine-readable failures

---

## Slide 4: Message Envelope

### Universal Structure

```json
{
  "type": "MESSAGE_TYPE",
  "subject": "optional.nats.subject",
  "payload": { ... },
  "headers": {
    "correlationId": "abc-123",
    "timestamp": "2024-01-15T10:30:00Z"
  }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| type | Yes | Message type for routing |
| subject | Sometimes | NATS subject for pub/sub |
| payload | Sometimes | Message data |
| headers | No | Metadata, tracing info |

---

## Slide 5: Message Types Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Message Types                         │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Authentication     Messaging          Control          │
│  ┌───────────┐     ┌───────────┐     ┌───────────┐     │
│  │   AUTH    │     │  PUBLISH  │     │   PING    │     │
│  │   AUTH_   │     │ SUBSCRIBE │     │   PONG    │     │
│  │  RESPONSE │     │  MESSAGE  │     │   ERROR   │     │
│  └───────────┘     │  REQUEST  │     └───────────┘     │
│                    │  RESPONSE │                        │
│                    └───────────┘                        │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 6: Authentication Flow

```
Device                        Gateway                      Backend
  │                             │                             │
  │──── WebSocket Connect ─────→│                             │
  │                             │                             │
  │◀─── Connection Accepted ────│                             │
  │                             │                             │
  │──── AUTH {deviceId, creds} ─│                             │
  │                             │──── Validate Credentials ──→│
  │                             │◀─── Valid/Invalid ─────────│
  │◀─── AUTH_RESPONSE ──────────│                             │
  │     {success, permissions}  │                             │
  │                             │                             │
  ├─────── Ready for Messages ──┼─────────────────────────────┤
```

---

## Slide 7: AUTH Message

### Device → Gateway

```json
{
  "type": "AUTH",
  "deviceId": "SENSOR-001",
  "credentials": {
    "apiKey": "sk_live_abc123..."
  }
}
```

### Credential Types Supported

| Type | Field | Use Case |
|------|-------|----------|
| API Key | apiKey | Production devices |
| JWT Token | token | OAuth integration |
| Certificate | certThumbprint | mTLS environments |

---

## Slide 8: AUTH_RESPONSE Message

### Gateway → Device

**Success:**
```json
{
  "type": "AUTH_RESPONSE",
  "success": true,
  "deviceId": "SENSOR-001",
  "permissions": {
    "publish": ["telemetry.SENSOR-001.>"],
    "subscribe": ["commands.SENSOR-001.>"]
  },
  "sessionId": "sess_xyz789"
}
```

**Failure:**
```json
{
  "type": "AUTH_RESPONSE",
  "success": false,
  "code": 401,
  "message": "Invalid API key"
}
```

---

## Slide 9: PUBLISH Message

### Device → Gateway → NATS

```json
{
  "type": "PUBLISH",
  "subject": "telemetry.SENSOR-001.temperature",
  "payload": {
    "value": 23.5,
    "unit": "C",
    "timestamp": "2024-01-15T10:30:00Z",
    "quality": "good"
  },
  "headers": {
    "correlationId": "msg-abc123",
    "contentType": "application/json"
  }
}
```

**Gateway Actions:**
1. Validate subject against permissions
2. Add gateway metadata headers
3. Publish to NATS
4. Optionally persist to JetStream

---

## Slide 10: SUBSCRIBE Message

### Device → Gateway

```json
{
  "type": "SUBSCRIBE",
  "subject": "commands.SENSOR-001.>"
}
```

### Wildcard Patterns

| Pattern | Matches |
|---------|---------|
| `commands.SENSOR-001.*` | Single token wildcard |
| `commands.SENSOR-001.>` | Multi-token wildcard |
| `commands.*.restart` | Any device, restart command |

---

## Slide 11: MESSAGE (Incoming)

### Gateway → Device (from NATS subscription)

```json
{
  "type": "MESSAGE",
  "subject": "commands.SENSOR-001.calibrate",
  "payload": {
    "action": "calibrate",
    "parameters": {
      "offset": 0.5,
      "reference": 25.0
    }
  },
  "headers": {
    "correlationId": "cmd-xyz789",
    "replyTo": "responses.SENSOR-001.calibrate"
  }
}
```

---

## Slide 12: REQUEST/RESPONSE Pattern

```
Device                        Gateway                      Service
  │                             │                             │
  │── REQUEST ─────────────────→│                             │
  │   {subject, payload,        │── NATS Request ───────────→│
  │    timeout}                 │                             │
  │                             │◀─ NATS Response ───────────│
  │◀─ RESPONSE ─────────────────│                             │
  │   {payload, correlationId}  │                             │
```

### Request Message
```json
{
  "type": "REQUEST",
  "subject": "services.config.get",
  "payload": { "key": "sampling_rate" },
  "timeout": 5000,
  "correlationId": "req-001"
}
```

### Response Message
```json
{
  "type": "RESPONSE",
  "correlationId": "req-001",
  "payload": { "sampling_rate": 1000 }
}
```

---

## Slide 13: Keep-Alive: PING/PONG

### Detecting Dead Connections

```
Device                        Gateway
  │                             │
  │────── PING ────────────────→│
  │                             │
  │◀───── PONG ─────────────────│
  │                             │
  ├─── 30 seconds ──────────────┤
  │                             │
  │────── PING ────────────────→│
```

```json
// PING
{ "type": "PING" }

// PONG
{ "type": "PONG", "timestamp": "2024-01-15T10:30:00Z" }
```

**Timeout:** No PONG within 10 seconds → Close connection

---

## Slide 14: ERROR Message

### Structured Error Reporting

```json
{
  "type": "ERROR",
  "code": 403,
  "message": "Permission denied",
  "details": "Cannot publish to 'admin.>' subjects",
  "correlationId": "msg-abc123"
}
```

### Error Codes

| Code | Meaning | Recovery |
|------|---------|----------|
| 400 | Bad Request | Fix message format |
| 401 | Unauthorized | Re-authenticate |
| 403 | Forbidden | Check permissions |
| 429 | Rate Limited | Slow down |
| 500 | Server Error | Retry later |

---

## Slide 15: Protocol State Machine

```
                    ┌─────────────┐
                    │ DISCONNECTED│
                    └──────┬──────┘
                           │ Connect
                           ▼
                    ┌─────────────┐
                    │  CONNECTED  │
                    └──────┬──────┘
                           │ Send AUTH
                           ▼
                    ┌─────────────┐
              ┌─────│AUTHENTICATING│─────┐
              │     └─────────────┘     │
         Failure                    Success
              │                         │
              ▼                         ▼
       ┌──────────┐             ┌───────────┐
       │  CLOSED  │◀────────────│   READY   │
       └──────────┘   Error/    └───────────┘
                     Timeout         │
                                     │ Graceful
                                     ▼
                              ┌──────────┐
                              │  CLOSED  │
                              └──────────┘
```

---

## Slide 16: Headers and Tracing

### Built-in Tracing Support

```json
{
  "type": "PUBLISH",
  "subject": "telemetry.sensor.temp",
  "payload": { "value": 23.5 },
  "headers": {
    "correlationId": "corr-abc123",
    "traceId": "trace-xyz789",
    "spanId": "span-001",
    "timestamp": "2024-01-15T10:30:00.123Z",
    "source": "SENSOR-001",
    "version": "1.0"
  }
}
```

**Gateway Adds:**
```json
{
  "gatewayId": "gateway-east-1",
  "receivedAt": "2024-01-15T10:30:00.125Z",
  "authenticated": true
}
```

---

## Slide 17: Binary Payload Support

### For Large Data or Efficiency

```json
{
  "type": "PUBLISH",
  "subject": "telemetry.sensor.image",
  "payloadEncoding": "base64",
  "payload": "iVBORw0KGgoAAAANSUhEUgAA...",
  "headers": {
    "contentType": "image/png",
    "size": 102400
  }
}
```

**Supported Encodings:**
- `json` (default) - Native JSON object
- `base64` - Binary data encoded
- `text` - Plain text string

---

## Slide 18: Reconnection Protocol

### Handling Network Interruptions

```
Device                        Gateway
  │                             │
  ├─── Connection Lost ─────────┤
  │                             │
  │ (Wait: exponential backoff) │
  │                             │
  │── Reconnect ───────────────→│
  │                             │
  │── AUTH (same deviceId) ────→│
  │                             │
  │◀─ AUTH_RESPONSE ────────────│
  │   {sessionId: "new-session"}│
  │                             │
  │── SUBSCRIBE (restore) ─────→│
  │   {subject: "commands.>"}   │
```

**Backoff Schedule:** 1s, 2s, 4s, 8s, 16s, 32s, 60s (max)

---

## Slide 19: Protocol Validation

### Gateway Validates Every Message

```csharp
public class MessageValidator
{
    public ValidationResult Validate(WebSocketMessage msg)
    {
        // Required fields
        if (string.IsNullOrEmpty(msg.Type))
            return Error("Missing 'type' field");

        // Type-specific validation
        switch (msg.Type)
        {
            case "PUBLISH":
                if (string.IsNullOrEmpty(msg.Subject))
                    return Error("PUBLISH requires 'subject'");
                if (!IsValidSubject(msg.Subject))
                    return Error("Invalid subject format");
                break;

            case "AUTH":
                if (msg.Credentials == null)
                    return Error("AUTH requires 'credentials'");
                break;
        }

        return Valid();
    }
}
```

---

## Slide 20: Next Episode Preview

# Episode 05: Device SDK (C++)

- C++ SDK architecture
- Connection management
- Offline buffering
- Metrics integration

**See you in the next episode!**
