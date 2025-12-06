# Gateway Protocol Specification v1.0

This document describes the WebSocket protocol between devices and the NATS WebSocket Bridge Gateway.

## Transport

- **Protocol**: WebSocket (RFC 6455)
- **URL Format**: `ws://` or `wss://` + host + `/ws`
- **Message Format**: UTF-8 JSON text frames
- **Binary**: Not supported

## Connection Lifecycle

```
┌──────────┐       ┌─────────┐
│  Device  │       │ Gateway │
└────┬─────┘       └────┬────┘
     │                  │
     │ WebSocket CONNECT│
     │─────────────────>│
     │                  │
     │ WebSocket ACCEPT │
     │<─────────────────│
     │                  │
     │ Auth Request     │
     │─────────────────>│
     │                  │
     │ Auth Response    │
     │<─────────────────│
     │                  │
     │ [Connected]      │
     │                  │
```

## Message Structure

All messages follow this JSON structure:

```json
{
  "type": <MessageType>,
  "subject": "<string>",
  "payload": <any>,
  "correlationId": "<string>",
  "timestamp": "<ISO8601>",
  "deviceId": "<string>"
}
```

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | int | Yes | Message type enum value |
| `subject` | string | For most types | NATS subject/topic |
| `payload` | any | No | JSON payload (object, array, primitive) |
| `correlationId` | string | No | For request/reply correlation |
| `timestamp` | string | No | ISO 8601 UTC timestamp |
| `deviceId` | string | No | Set by gateway, not by device |

### Message Types

| Name | Value | Direction | Description |
|------|-------|-----------|-------------|
| Publish | 0 | Device → Gateway | Publish message to NATS |
| Subscribe | 1 | Device → Gateway | Subscribe to subject |
| Unsubscribe | 2 | Device → Gateway | Unsubscribe from subject |
| Message | 3 | Gateway → Device | Received message from subscription |
| Request | 4 | Device → Gateway | Request/reply (with correlationId) |
| Reply | 5 | Gateway → Device | Reply to request |
| Ack | 6 | Gateway → Device | Acknowledgment of operation |
| Error | 7 | Gateway → Device | Error message |
| Auth | 8 | Bidirectional | Authentication handshake |
| Ping | 9 | Device → Gateway | Keep-alive ping |
| Pong | 10 | Gateway → Device | Keep-alive pong |

## Authentication

### Auth Request (Device → Gateway)

Must be sent immediately after WebSocket connection.

```json
{
  "type": 8,
  "payload": {
    "deviceId": "sensor-001",
    "token": "api-key-or-token",
    "deviceType": "sensor"
  }
}
```

### Auth Response (Gateway → Device)

#### Success

```json
{
  "type": 8,
  "payload": {
    "success": true,
    "device": {
      "deviceId": "sensor-001",
      "deviceType": "sensor",
      "isConnected": true,
      "connectedAt": "2024-01-15T10:00:00Z",
      "allowedPublishTopics": ["telemetry.sensor-001.>", "alerts.sensor-001.>"],
      "allowedSubscribeTopics": ["commands.sensor-001.>", "config.sensor-001.>"]
    }
  }
}
```

#### Failure

```json
{
  "type": 8,
  "payload": {
    "success": false,
    "message": "Invalid credentials"
  }
}
```

### Authentication Timeout

- Default: 30 seconds
- Device must complete authentication within timeout
- Gateway closes connection on timeout

## Publish

### Request

```json
{
  "type": 0,
  "subject": "telemetry.sensor-001.temperature",
  "payload": {
    "value": 25.5,
    "unit": "celsius"
  },
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

### Response

No direct response. Errors are sent as Error messages.

## Subscribe

### Request

```json
{
  "type": 1,
  "subject": "commands.sensor-001.>"
}
```

### Acknowledgment

```json
{
  "type": 6,
  "subject": "commands.sensor-001.>",
  "payload": {
    "success": true,
    "message": "Subscribed successfully"
  }
}
```

### Received Messages

When messages arrive on subscribed subjects:

```json
{
  "type": 3,
  "subject": "commands.sensor-001.restart",
  "payload": {
    "action": "restart",
    "reason": "maintenance"
  },
  "timestamp": "2024-01-15T10:35:00.000Z",
  "deviceId": "controller-001"
}
```

## Unsubscribe

### Request

```json
{
  "type": 2,
  "subject": "commands.sensor-001.>"
}
```

## Heartbeat

### Ping (Device → Gateway)

```json
{
  "type": 9
}
```

### Pong (Gateway → Device)

```json
{
  "type": 10
}
```

### Timing

- Default interval: 30 seconds
- Default timeout: 10 seconds
- Missed pongs before disconnect: 2

## Error Messages

### Format

```json
{
  "type": 7,
  "payload": {
    "message": "Rate limit exceeded",
    "code": "RATE_LIMIT"
  }
}
```

### Error Codes

| Code | Description |
|------|-------------|
| `AUTH_FAILED` | Authentication failed |
| `AUTH_TIMEOUT` | Authentication timeout |
| `NOT_AUTHORIZED` | Not authorized for operation |
| `INVALID_SUBJECT` | Invalid subject format |
| `PAYLOAD_TOO_LARGE` | Payload exceeds limit |
| `RATE_LIMIT` | Rate limit exceeded |
| `INTERNAL_ERROR` | Internal server error |

## Subject Format

### Rules

- Maximum length: 256 characters
- Cannot start or end with `.`
- Cannot contain `..`
- Allowed characters: `a-z`, `A-Z`, `0-9`, `.`, `*`, `>`, `-`, `_`

### Wildcards

| Wildcard | Meaning | Example |
|----------|---------|---------|
| `*` | Single token | `sensors.*.temperature` matches `sensors.room1.temperature` |
| `>` | Multiple tokens (end only) | `sensors.>` matches `sensors.room1.temp` and `sensors.room1.humidity` |

### Recommended Patterns

```
telemetry.{deviceId}.{metric}     # Sensor readings
commands.{deviceId}.{action}      # Device commands
status.{deviceId}                 # Device status
alerts.{deviceId}.{severity}      # Alerts
config.{deviceId}                 # Configuration
heartbeat.{deviceId}              # Heartbeats
responses.{deviceId}.{correlationId}  # Request responses
```

## Rate Limiting

- Default: 100 messages/second per device
- Exceeding limit results in Error message
- Temporarily blocks further messages

## Payload Size

- Maximum: 1 MB (default)
- Configurable per gateway
- Exceeding limit results in Error message

## Reconnection

### Recommendations

1. Exponential backoff starting at 1 second
2. Maximum delay of 30-60 seconds
3. Add jitter (±25%) to prevent thundering herd
4. Resubscribe to all topics after reconnection
5. Re-authenticate on each connection

### Example Backoff

| Attempt | Base Delay | With Jitter |
|---------|------------|-------------|
| 1 | 1s | 0.75s - 1.25s |
| 2 | 2s | 1.5s - 2.5s |
| 3 | 4s | 3s - 5s |
| 4 | 8s | 6s - 10s |
| 5+ | 30s | 22.5s - 37.5s |

## TLS Requirements

### Production

- TLS 1.2 or higher
- Valid server certificate
- Certificate verification enabled

### Development

- Self-signed certificates allowed
- Hostname verification can be disabled
- NOT recommended for production

## Timestamps

- Format: ISO 8601
- Timezone: UTC (Z suffix)
- Precision: Milliseconds recommended

```
2024-01-15T10:30:00.000Z
```

## Version Negotiation

Future versions may include version negotiation in the auth request:

```json
{
  "type": 8,
  "payload": {
    "deviceId": "sensor-001",
    "token": "...",
    "deviceType": "sensor",
    "protocolVersion": "1.0",
    "sdkVersion": "1.0.0"
  }
}
```
