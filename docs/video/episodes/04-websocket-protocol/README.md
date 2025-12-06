# Episode 04: WebSocket Protocol

**Duration:** 10-12 minutes
**Prerequisites:** Episodes 01-03

## Learning Objectives

By the end of this episode, viewers will understand:
- WebSocket authentication flow
- Message format design (JSON envelope)
- Publish, subscribe, and request patterns
- Error handling and reconnection

## Outline

1. **Protocol Overview** (0:00-1:30)
   - Why define a custom protocol
   - Message envelope structure
   - Type field for routing

2. **Authentication Flow** (1:30-4:00)
   - Connection establishment
   - AUTH message with credentials
   - AUTH_RESPONSE success/failure
   - Token-based vs API key authentication
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

```json
// Authentication
{
  "type": "AUTH",
  "deviceId": "SENSOR-001",
  "credentials": {
    "apiKey": "sk_live_..."
  }
}

// Publish Telemetry
{
  "type": "PUBLISH",
  "subject": "factory.line1.sensor.temp",
  "payload": {
    "value": 23.5,
    "unit": "C",
    "timestamp": "2024-01-15T10:30:00Z"
  },
  "headers": {
    "correlationId": "abc-123"
  }
}

// Subscribe to Commands
{
  "type": "SUBSCRIBE",
  "subject": "commands.SENSOR-001.>"
}

// Error Response
{
  "type": "ERROR",
  "code": 401,
  "message": "Authentication failed",
  "details": "Invalid API key"
}
```

## Demo

```bash
# Connect and authenticate
wscat -c ws://localhost:5000/ws

# Send auth message
{"type":"AUTH","deviceId":"demo-001","credentials":{"apiKey":"test-key"}}

# Publish telemetry
{"type":"PUBLISH","subject":"factory.line1.sensor.temp","payload":{"value":23.5}}

# Subscribe to commands
{"type":"SUBSCRIBE","subject":"commands.demo-001.>"}
```

## Key Visuals

- Authentication sequence diagram
- Message type decision tree
- JSON schema visualization
- Error code reference table
