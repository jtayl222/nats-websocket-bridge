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
# Type this in wscat:
{"type":"AUTH","deviceId":"SENSOR-001","credentials":{"apiKey":"sk_test_key123"}}

# Expected response:
# {"type":"AUTH_RESPONSE","success":true,"deviceId":"SENSOR-001","permissions":{"publish":["telemetry.SENSOR-001.>"],"subscribe":["commands.SENSOR-001.>"]}}
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

# Send invalid credentials
{"type":"AUTH","deviceId":"SENSOR-001","credentials":{"apiKey":"invalid-key"}}

# Expected response:
# {"type":"AUTH_RESPONSE","success":false,"code":401,"message":"Invalid API key"}

# Connection will be closed by gateway
```

---

## Demo 3: Publishing Telemetry

```bash
# In authenticated wscat session:

# Simple telemetry message
{"type":"PUBLISH","subject":"telemetry.SENSOR-001.temperature","payload":{"value":23.5,"unit":"C"}}

# With headers for tracing
{"type":"PUBLISH","subject":"telemetry.SENSOR-001.pressure","payload":{"value":101.3,"unit":"kPa"},"headers":{"correlationId":"msg-001","timestamp":"2024-01-15T10:30:00Z"}}

# Watch Terminal 3 (nats sub) for messages
```

---

## Demo 4: Permission Enforcement

```bash
# Try to publish to unauthorized subject
{"type":"PUBLISH","subject":"admin.system.restart","payload":{"force":true}}

# Expected error response:
# {"type":"ERROR","code":403,"message":"Permission denied","details":"Cannot publish to 'admin.>' subjects"}
```

---

## Demo 5: Subscribe to Commands

```bash
# Subscribe to device commands
{"type":"SUBSCRIBE","subject":"commands.SENSOR-001.>"}

# Subscribe confirmation (implicit - no error means success)

# Terminal 5: Send a command via NATS CLI
nats pub commands.SENSOR-001.calibrate '{"action":"calibrate","offset":0.5}'

# Watch wscat for incoming MESSAGE:
# {"type":"MESSAGE","subject":"commands.SENSOR-001.calibrate","payload":{"action":"calibrate","offset":0.5}}
```

---

## Demo 6: Wildcard Subscriptions

```bash
# Subscribe with single-token wildcard
{"type":"SUBSCRIBE","subject":"commands.SENSOR-001.*"}

# Subscribe with multi-token wildcard
{"type":"SUBSCRIBE","subject":"alerts.>"}

# Test different patterns
nats pub commands.SENSOR-001.restart '{}'
nats pub commands.SENSOR-001.config.update '{}'
nats pub alerts.factory.line1.critical '{"message":"Temperature high"}'
```

---

## Demo 7: Request/Response

```bash
# In wscat, send a request
{"type":"REQUEST","subject":"services.config.get","payload":{"key":"sampling_rate"},"timeout":5000,"correlationId":"req-001"}

# Terminal 5: Start a responder service
nats reply services.config.get '{"sampling_rate":1000}'

# Watch wscat for response:
# {"type":"RESPONSE","correlationId":"req-001","payload":{"sampling_rate":1000}}
```

---

## Demo 8: Keep-Alive Ping/Pong

```bash
# In wscat, send ping
{"type":"PING"}

# Immediate response:
# {"type":"PONG","timestamp":"2024-01-15T10:30:00Z"}

# Gateway also sends periodic pings - watch for them
# If device doesn't respond, connection is closed
```

---

## Demo 9: Error Scenarios

```bash
# Missing required field
{"type":"PUBLISH","payload":{"value":23.5}}
# Response: {"type":"ERROR","code":400,"message":"Bad Request","details":"PUBLISH requires 'subject' field"}

# Invalid message type
{"type":"UNKNOWN","data":"test"}
# Response: {"type":"ERROR","code":400,"message":"Unknown message type: UNKNOWN"}

# Invalid JSON
{invalid json here
# Response: {"type":"ERROR","code":400,"message":"Invalid JSON format"}

# Publish before auth
# (new connection without AUTH)
{"type":"PUBLISH","subject":"test","payload":{}}
# Response: {"type":"ERROR","code":401,"message":"Not authenticated"}
```

---

## Demo 10: Rate Limiting

```bash
# Send many messages quickly (script)
for i in {1..100}; do
  echo '{"type":"PUBLISH","subject":"test.rapid","payload":{"i":'$i'}}'
done | wscat -c ws://localhost:5000/ws

# After threshold, expect:
# {"type":"ERROR","code":429,"message":"Rate limit exceeded","details":"Max 100 messages per second"}
```

---

## Demo 11: Headers and Tracing

```bash
# Publish with full tracing headers
{"type":"PUBLISH","subject":"telemetry.SENSOR-001.data","payload":{"value":42},"headers":{"correlationId":"corr-123","traceId":"trace-abc","spanId":"span-001","source":"factory-floor","version":"1.2.0"}}

# Check NATS message headers
nats sub "telemetry.>" --headers

# Headers are preserved through the gateway
```

---

## Demo 12: Binary Payload (Base64)

```bash
# Send binary data as base64
{"type":"PUBLISH","subject":"telemetry.SENSOR-001.image","payloadEncoding":"base64","payload":"SGVsbG8gV29ybGQh","headers":{"contentType":"application/octet-stream"}}

# "SGVsbG8gV29ybGQh" is "Hello World!" in base64

# Receive and decode
nats sub "telemetry.SENSOR-001.image" | base64 -d
```

---

## Demo 13: Multiple Subscriptions

```bash
# Subscribe to multiple subjects
{"type":"SUBSCRIBE","subject":"commands.SENSOR-001.>"}
{"type":"SUBSCRIBE","subject":"alerts.factory.>"}
{"type":"SUBSCRIBE","subject":"config.updates"}

# Send to each
nats pub commands.SENSOR-001.restart '{}'
nats pub alerts.factory.line1.warning '{"msg":"low pressure"}'
nats pub config.updates '{"version":"2.0"}'

# All three should appear in wscat
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

### "Not authenticated" on every message
- Ensure AUTH is sent first after connection
- Check API key is valid
- Verify AUTH_RESPONSE shows success:true

### Messages not reaching NATS
- Check subject matches permissions
- Verify NATS subscription pattern
- Look at gateway logs for errors

### Connection drops unexpectedly
- Check for PING/PONG timeout
- Verify network stability
- Check gateway idle timeout setting
