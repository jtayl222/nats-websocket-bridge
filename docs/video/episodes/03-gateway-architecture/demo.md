# Episode 03: Gateway Architecture - Demo Script

## Demo 1: Project Structure Walkthrough

```bash
# Show the project structure
cd src/NatsWebSocketBridge.Gateway

# List main directories
ls -la

# Show configuration files
cat appsettings.json | head -50

# Show the Program.cs entry point
head -80 Program.cs
```

**Talking Points:**
- Point out the clean separation of concerns
- Highlight the configuration-driven design
- Show how DI is set up

---

## Demo 2: Start the Infrastructure

```bash
# Start NATS server
docker run -d --name nats \
  -p 4222:4222 \
  -p 8222:8222 \
  nats:latest -js -m 8222

# Verify NATS is running
nats server info

# Check JetStream status
nats stream ls
```

---

## Demo 3: Run the Gateway

```bash
# Terminal 1: Start the gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Watch for startup messages:
# - "Gateway starting..."
# - "Connected to NATS at nats://localhost:4222"
# - "JetStream streams initialized"
# - "Listening on http://0.0.0.0:5000"
```

---

## Demo 4: Monitor NATS Traffic

```bash
# Terminal 2: Subscribe to all messages
nats sub ">"

# Alternative: Subscribe to specific patterns
nats sub "telemetry.>"
nats sub "factory.line1.>"
```

---

## Demo 5: Generate a JWT Token for Testing

The gateway now uses JWT authentication. You need a valid JWT token to connect.

```bash
# In Development mode, the gateway provides a token generation endpoint
# POST /dev/token

# Generate a token for a demo device with full access:
curl -s -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"demo-device","role":"sensor","publish":["telemetry.>","factory.>"],"subscribe":["commands.demo-device.>"]}' \
  | jq -r '.token'

# Generate a token with default permissions (full access):
curl -s -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"demo-device"}' \
  | jq -r '.token'

# Generate a token with custom expiry (24 hours):
curl -s -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"demo-device","expiryHours":24}' \
  | jq -r '.token'

# Save the token for use in demos:
TOKEN=$(curl -s -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"demo-device","role":"sensor","publish":["telemetry.>","factory.>"],"subscribe":["commands.demo-device.>"]}' \
  | jq -r '.token')
echo $TOKEN
```

**Note:** The `/dev/token` endpoint is only available in Development mode for security reasons.

---

## Demo 6: Connect a WebSocket Client

```bash
# Terminal 3: Connect with wscat
wscat -c ws://localhost:5000/ws

# After connection, authenticate with JWT (type 8 = Auth):
# Replace <JWT_TOKEN> with the token generated in Demo 5
{"type":8,"payload":{"token":"<JWT_TOKEN>"}}

# Expected response:
# {"type":8,"payload":{"success":true,"clientId":"demo-device","role":"sensor"}}

# Example with a real token (generate fresh one as shown above):
{"type":8,"payload":{"token":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."}}
```

**Key Differences from Old Auth:**
- Old: `{"type":8,"payload":{"deviceId":"demo-device","token":"demo-token"}}`
- New: `{"type":8,"payload":{"token":"<JWT>"}}`
- The JWT contains the device ID, role, and permissions

---

## Demo 7: Publish Messages Through Gateway

```bash
# In wscat session, publish telemetry (type 0 = Publish):
{"type":0,"subject":"telemetry.demo-device.temperature","payload":{"value":23.5,"unit":"C"}}

# Publish to a specific factory line:
{"type":0,"subject":"factory.line1.sensor.pressure","payload":{"value":101.3,"unit":"kPa"}}

# Watch Terminal 2 (nats sub) for the messages appearing

# Message Types Reference:
# 0 = Publish, 1 = Subscribe, 2 = Unsubscribe, 3 = Message (incoming)
# 8 = Auth, 9 = Ping, 10 = Pong, 7 = Error

# Note: Publishing only works if the topic matches your JWT "pub" claim patterns
# If unauthorized, you'll receive: {"type":7,"payload":{"error":"Not authorized to publish to subject"}}
```

---

## Demo 8: Subscribe to Commands

```bash
# In wscat session, subscribe to commands (type 1 = Subscribe):
{"type":1,"subject":"commands.demo-device.>"}

# You should receive an ACK (type 6):
# {"type":6,"subject":"commands.demo-device.>","correlationId":null,...}

# Terminal 4: Send a command via NATS CLI
nats pub commands.demo-device.restart '{"action":"restart","force":false}'

# Watch wscat for the incoming MESSAGE (type 3)

# Note: Subscribing only works if the topic matches your JWT "subscribe" claim patterns
```

---

## Demo 9: View Metrics

```bash
# Check the metrics endpoint
curl -s http://localhost:5000/metrics | head -50

# Look for key metrics:
# gateway_connections_active
# gateway_messages_received_total
# gateway_nats_publish_duration_seconds

# Pretty print specific metrics
curl -s http://localhost:5000/metrics | grep gateway_connections
```

---

## Demo 10: View Connected Devices

```bash
# Check the devices endpoint
curl -s http://localhost:5000/devices | jq

# Expected output:
# [
#   {
#     "clientId": "demo-device",
#     "role": "sensor",
#     "connectedAt": "2024-01-15T10:30:00Z",
#     "expiresAt": "2024-01-22T10:30:00Z"
#   }
# ]
```

---

## Demo 11: Test Multiple Connections

```bash
# Generate tokens for different devices with different permissions

# Device 1: Sensor (can publish telemetry, subscribe to commands)
# JWT claims: sub="sensor-001", role="sensor", pub=["telemetry.>"], subscribe=["commands.sensor-001.>"]

# Device 2: Actuator (can subscribe to commands, publish status)
# JWT claims: sub="actuator-001", role="actuator", pub=["status.>"], subscribe=["commands.actuator-001.>"]

# Device 3: Admin (full access)
# JWT claims: sub="admin-001", role="admin", pub=["*"], subscribe=["*"]

# Connect each in separate terminals and verify permissions are enforced
```

---

## Demo 12: Graceful Shutdown

```bash
# In Gateway terminal, press Ctrl+C

# Watch for shutdown messages:
# - "Shutdown requested..."
# - "Closing X active connections..."
# - "Disconnected from NATS"
# - "Gateway stopped"

# Verify NATS shows disconnection
nats server info
```

---

## Demo 13: Code Walkthrough - JWT Auth Service

```bash
# Open the JWT auth service
cat Auth/JwtDeviceAuthService.cs

# Key sections to highlight:
# 1. ValidateToken - JWT validation and DeviceContext extraction
# 2. CanPublish/CanSubscribe - Permission checking with wildcard support
# 3. GenerateToken - For testing/development
# 4. MatchesSubject - NATS wildcard pattern matching (* and >)
```

---

## Demo 14: Code Walkthrough - DeviceContext

```bash
# Open the DeviceContext model
cat Models/DeviceContext.cs

# Key points:
# 1. Immutable record with device identity
# 2. AllowedPublish/AllowedSubscribe for authorization
# 3. ExpiresAt for token expiration
# 4. IsExpired computed property
```

---

## Cleanup

```bash
# Stop the gateway (Ctrl+C)

# Stop NATS
docker stop nats && docker rm nats

# Or if using docker-compose
docker-compose down
```

---

## Troubleshooting

### Gateway won't start
```bash
# Check if port is in use
lsof -i :5000

# Check NATS is reachable
nc -zv localhost 4222
```

### WebSocket connection refused
```bash
# Verify gateway is listening
curl -I http://localhost:5000/health

# Check WebSocket upgrade
curl -i -N \
  -H "Connection: Upgrade" \
  -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  -H "Sec-WebSocket-Version: 13" \
  http://localhost:5000/ws
```

### Authentication fails
```bash
# Check JWT token is valid
# - Verify signature matches Jwt:Secret in appsettings.json
# - Check token hasn't expired (exp claim)
# - Verify issuer/audience match config

# Common errors:
# "Token is required" - payload.token is missing
# "Token expired" - exp claim is in the past
# "Token validation failed" - wrong secret or malformed JWT
```

### Messages not appearing in NATS
```bash
# Check if JetStream is consuming
nats consumer info TELEMETRY gateway-consumer

# Check stream status
nats stream info TELEMETRY
```

### Authorization denied
```bash
# Check your JWT claims:
# - "pub" claim must include a pattern matching your publish subject
# - "subscribe" claim must include a pattern matching your subscribe subject

# Wildcard patterns:
# "telemetry.>" matches telemetry.sensor.temp, telemetry.sensor.pressure, etc.
# "telemetry.*" matches telemetry.sensor but NOT telemetry.sensor.temp
```
