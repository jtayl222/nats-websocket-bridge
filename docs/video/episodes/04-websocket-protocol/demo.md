# Episode 04: WebSocket Protocol - Demo Script

## Setup

```bash
# Terminal 1: Start NATS
docker run -d --name nats -p 4222:4222 -p 8222:8222 nats:latest -js -m 8222

# Terminal 2: Start the Gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Terminal 3: Monitor NATS traffic
nats sub ">"
```

---

## Demo 1: Authentication Flow

```bash
# Terminal 4: Connect with wscat
wscat -c ws://localhost:5000/ws

# Connection established - now authenticate
# Message types: 0=Publish, 1=Subscribe, 8=Auth, 9=Ping, 7=Error
# Type this in wscat (type 8 = Auth):
{"type":8,"payload":{"deviceId":"SENSOR-001","token":"sensor-token"}}

# Expected response (type 8 with success):
# {"type":8,"payload":{"success":true,"device":{"deviceId":"SENSOR-001","deviceType":"sensor",...}}}

# Available demo devices:
# demo-device / demo-token - broad permissions for testing
# test-device / test-token - general testing
# SENSOR-001 / sensor-token - sensor simulation
# sensor-temp-001 / temp-sensor-token-001 - temperature sensor
```

**Talking Points:**
- Connection is established but not authenticated yet
- Device must send AUTH within timeout (default 30s)
- Response includes permissions for this device

---

## Demo 2: Failed Authentication

```bash
# In a new wscat session:
wscat -c ws://localhost:5000/ws

# Send invalid credentials (wrong token)
{"type":8,"payload":{"deviceId":"SENSOR-001","token":"wrong-token"}}

# Expected response (type 8 with error):
# {"type":8,"payload":{"success":false,"error":"Invalid token"}}

# Connection will be closed by gateway

# Try unregistered device:
{"type":8,"payload":{"deviceId":"unknown-device","token":"any-token"}}
# Response: {"type":8,"payload":{"success":false,"error":"Device not registered"}}
```

---

## Demo 3: Publishing Telemetry

```bash
# In authenticated wscat session (after auth with SENSOR-001):

# Simple telemetry message (type 0 = Publish)
{"type":0,"subject":"telemetry.SENSOR-001.temperature","payload":{"value":23.5,"unit":"C"}}

# With correlation ID for tracing
{"type":0,"subject":"telemetry.SENSOR-001.pressure","payload":{"value":101.3,"unit":"kPa"},"correlationId":"msg-001"}

# Watch Terminal 3 (nats sub) for messages
```

---

## Demo 4: Permission Enforcement

```bash
# Try to publish to unauthorized subject (type 0 = Publish)
{"type":0,"subject":"admin.system.restart","payload":{"force":true}}

# Expected error response (type 7 = Error):
# {"type":7,"payload":{"error":"Not authorized to publish to admin.system.restart"}}
```

---

## Demo 5: Subscribe to Commands

```bash
# Subscribe to device commands (type 1 = Subscribe)
{"type":1,"subject":"commands.SENSOR-001.>"}

# Subscribe confirmation (type 6 = Ack):
# {"type":6,"subject":"commands.SENSOR-001.>","correlationId":null,...}

# Terminal 5: Send a command via NATS CLI
nats pub commands.SENSOR-001.calibrate '{"action":"calibrate","offset":0.5}'

# Watch wscat for incoming MESSAGE (type 3):
# {"type":3,"subject":"commands.SENSOR-001.calibrate","payload":{...}}
```

---

## Demo 6: Wildcard Subscriptions

```bash
# Subscribe with single-token wildcard (type 1 = Subscribe)
{"type":1,"subject":"commands.SENSOR-001.*"}

# Subscribe with multi-token wildcard
{"type":1,"subject":"factory.line1.>"}

# Test different patterns
nats pub commands.SENSOR-001.restart '{}'
nats pub factory.line1.alert '{"message":"Temperature high"}'
```

---

## Demo 7: Request/Response

```bash
# Note: Request/Response uses type 4 (Request)
# This requires a NATS responder service

# Terminal 5: Start a responder service first
nats reply services.config.get '{"sampling_rate":1000}'

# In wscat, send a request (type 4 = Request)
{"type":4,"subject":"services.config.get","payload":{"key":"sampling_rate"},"correlationId":"req-001"}

# Watch wscat for response (type 5 = Reply):
# {"type":5,"correlationId":"req-001","payload":{"sampling_rate":1000}}
```

---

## Demo 8: Keep-Alive Ping/Pong

```bash
# In wscat, send ping (type 9 = Ping)
{"type":9}

# Immediate response (type 10 = Pong):
# {"type":10,"timestamp":"2024-01-15T10:30:00Z"}

# Gateway also sends periodic pings - watch for them
# If device doesn't respond, connection is closed
```

---

## Demo 9: Error Scenarios

```bash
# Missing required field (type 0 = Publish without subject)
{"type":0,"payload":{"value":23.5}}
# Response (type 7 = Error): {"type":7,"payload":{"error":"Subject is required"}}

# Invalid message type
{"type":99,"data":"test"}
# Response: {"type":7,"payload":{"error":"Invalid message format"}}

# Invalid JSON
{invalid json here
# Connection may be closed or error returned

# Publish before auth (new connection without AUTH)
{"type":0,"subject":"test","payload":{}}
# Response: {"type":7,"payload":{"error":"Authentication failed"}}
# Connection closed
```

---

## Demo 10: Rate Limiting

```bash
# After authenticating, send many messages quickly
# In wscat, paste rapidly:
{"type":0,"subject":"test.rapid","payload":{"i":1}}
{"type":0,"subject":"test.rapid","payload":{"i":2}}
# ... continue rapidly

# After threshold (100/sec default), expect:
# {"type":7,"payload":{"error":"Rate limit exceeded"}}
```

---

## Demo 11: Correlation IDs and Tracing

```bash
# Publish with correlation ID for tracing (type 0 = Publish)
{"type":0,"subject":"telemetry.SENSOR-001.data","payload":{"value":42},"correlationId":"corr-123"}

# Check NATS messages
nats sub "telemetry.>"

# The gateway adds deviceId and timestamp automatically
```

---

## Demo 12: JSON Payloads

```bash
# Payload can contain any valid JSON (type 0 = Publish)
{"type":0,"subject":"telemetry.SENSOR-001.complex","payload":{"readings":[1.2,3.4,5.6],"metadata":{"unit":"C","quality":"good"}}}

# Arrays work too
{"type":0,"subject":"telemetry.SENSOR-001.batch","payload":[{"t":1,"v":23.5},{"t":2,"v":23.7}]}

# Check NATS
nats sub "telemetry.SENSOR-001.>"
```

---

## Demo 13: Multiple Subscriptions

```bash
# Subscribe to multiple subjects (type 1 = Subscribe)
{"type":1,"subject":"commands.SENSOR-001.>"}
{"type":1,"subject":"factory.line1.>"}

# Each subscription returns an ACK (type 6)

# Send to each
nats pub commands.SENSOR-001.restart '{}'
nats pub factory.line1.alert '{"msg":"low pressure"}'

# Both messages should appear in wscat as type 3 (Message)
```

---

## Demo 14: Graceful Disconnect

```bash
# Normal close (Ctrl+C in wscat)
# Gateway logs: "Device SENSOR-001 disconnected gracefully"

# Abrupt close (kill the terminal)
# Gateway logs: "Device SENSOR-001 connection lost"
# Gateway cleans up subscriptions and resources
```

---

## Demo 15: Protocol Documentation Check

```bash
# View the message types enum in code
cat Models/WebSocketMessage.cs

# View the handler routing
grep -A 20 "switch.*Type" Services/WebSocketHandler.cs

# Validate against documentation
cat ../../../docs/api/WEBSOCKET_PROTOCOL.md
```

---

## Cleanup

```bash
# Close wscat sessions (Ctrl+C)

# Stop gateway (Ctrl+C)

# Stop NATS
docker stop nats && docker rm nats
```

---

## Troubleshooting

### "Authentication failed" on every message
- Ensure AUTH (type 8) is sent first after connection
- Use correct format: `{"type":8,"payload":{"deviceId":"...","token":"..."}}`
- Use registered device: demo-device/demo-token, SENSOR-001/sensor-token
- Verify response shows `"success":true`

### Messages not reaching NATS
- Check subject matches device's allowed publish topics
- Verify NATS subscription pattern matches
- Look at gateway logs for authorization errors

### Connection drops unexpectedly
- Check for PING/PONG timeout (send type 9 to keep alive)
- Verify network stability
- Check gateway idle timeout setting (default 30s auth timeout)

### Message Type Reference
- 0: Publish, 1: Subscribe, 2: Unsubscribe
- 3: Message (incoming), 4: Request, 5: Reply
- 6: Ack, 7: Error, 8: Auth, 9: Ping, 10: Pong
