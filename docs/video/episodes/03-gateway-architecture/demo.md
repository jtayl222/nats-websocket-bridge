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

## Demo 5: Connect a WebSocket Client

```bash
# Terminal 3: Connect with wscat
wscat -c ws://localhost:5000/ws

# After connection, authenticate (type 8 = Auth):
{"type":8,"payload":{"deviceId":"demo-device","token":"demo-token"}}

# Expected response:
# {"type":8,"payload":{"success":true,"device":{"deviceId":"demo-device",...}}}

# Alternative devices to test:
# {"type":8,"payload":{"deviceId":"SENSOR-001","token":"sensor-token"}}
# {"type":8,"payload":{"deviceId":"sensor-temp-001","token":"temp-sensor-token-001"}}
```

---

## Demo 6: Publish Messages Through Gateway

```bash
# In wscat session, publish telemetry (type 0 = Publish):
{"type":0,"subject":"telemetry.demo-device.temperature","payload":{"value":23.5,"unit":"C"}}

# Publish to a specific factory line:
{"type":0,"subject":"factory.line1.sensor.pressure","payload":{"value":101.3,"unit":"kPa"}}

# Watch Terminal 2 (nats sub) for the messages appearing

# Message Types Reference:
# 0 = Publish, 1 = Subscribe, 2 = Unsubscribe, 3 = Message (incoming)
# 8 = Auth, 9 = Ping, 10 = Pong, 7 = Error
```

---

## Demo 7: Subscribe to Commands

```bash
# In wscat session, subscribe to commands (type 1 = Subscribe):
{"type":1,"subject":"commands.demo-device.>"}

# You should receive an ACK (type 6):
# {"type":6,"subject":"commands.demo-device.>","correlationId":null,...}

# Terminal 4: Send a command via NATS CLI
nats pub commands.demo-device.restart '{"action":"restart","force":false}'

# Watch wscat for the incoming MESSAGE (type 3)
```

---

## Demo 8: View Metrics

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

## Demo 9: Test Connection Limits

```bash
# Note: For multiple connections, use pre-registered devices
# The demo-device and test-device are available for testing

# Connect with demo-device
wscat -c ws://localhost:5000/ws -x '{"type":8,"payload":{"deviceId":"demo-device","token":"demo-token"}}'

# In another terminal, connect with test-device
wscat -c ws://localhost:5000/ws -x '{"type":8,"payload":{"deviceId":"test-device","token":"test-token"}}'

# Check active connections
curl -s http://localhost:5000/metrics | grep gateway_connections_active
```

---

## Demo 10: Graceful Shutdown

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

## Demo 11: Code Walkthrough - WebSocket Handler

```bash
# Open the WebSocket handler
cat Services/WebSocketHandler.cs

# Key sections to highlight:
# 1. HandleAsync method - main entry point
# 2. ProcessMessagesAsync - message loop
# 3. RouteMessageAsync - type-based dispatch
# 4. Client tracking with ConcurrentDictionary
```

---

## Demo 12: Code Walkthrough - NATS Service

```bash
# Open the NATS service
cat Services/NatsService.cs

# Key sections:
# 1. Connection management
# 2. PublishAsync with metrics
# 3. SubscribeAsync with IAsyncEnumerable
# 4. Error handling and logging
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

### Messages not appearing in NATS
```bash
# Check if JetStream is consuming
nats consumer info TELEMETRY gateway-consumer

# Check stream status
nats stream info TELEMETRY
```
