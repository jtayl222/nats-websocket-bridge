# Episode 05: Device SDK (C++) - Demo Script

## Setup

```bash
# Terminal 1: Start infrastructure
docker run -d --name nats -p 4222:4222 nats:latest -js

# Terminal 2: Start Gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Terminal 3: Monitor NATS
nats sub ">"
```

---

## Demo 1: SDK Project Structure

```bash
# Navigate to SDK directory
cd sdk/cpp

# Show the structure
tree -L 2

# Key files
cat include/gateway/client.h | head -60
cat include/gateway/options.h
```

**Talking Points:**
- Header-only interfaces in include/
- Implementation in src/
- Examples for quick start

---

## Demo 2: Build the SDK

```bash
# Create build directory
mkdir -p build && cd build

# Configure with CMake
cmake ..

# Build the SDK and examples
make -j4

# Check built artifacts
ls -la libgateway-sdk.a
ls -la examples/sensor_demo
```

---

## Demo 3: Run Sensor Demo

```bash
# Run the sensor demo (sends telemetry every second)
./examples/sensor_demo

# Output shows:
# [INFO] Connecting to ws://localhost:5000/ws...
# [INFO] Connected and authenticated
# [INFO] Publishing temperature: 23.5°C
# [INFO] Publishing temperature: 23.7°C
# ...

# Watch Terminal 3 for NATS messages
```

---

## Demo 4: Connection Options Walkthrough

```cpp
// Show the example code
cat ../examples/sensor_demo.cpp
```

```cpp
// Key configuration points:
gateway::ConnectionOptions opts;
opts.serverUrl = "ws://localhost:5000/ws";
opts.deviceId = "SENSOR-001";
opts.apiKey = "sk_test_key";

// Reconnection settings
opts.autoReconnect = true;
opts.initialReconnectDelay = std::chrono::seconds(1);
opts.maxReconnectDelay = std::chrono::seconds(60);

// Buffer for offline operation
opts.bufferSize = 1000;
opts.overflowPolicy = gateway::OverflowPolicy::DropOldest;
```

---

## Demo 5: Simulate Offline Operation

```bash
# Terminal 4: Run sensor demo
./examples/sensor_demo

# Stop the gateway (Ctrl+C in Terminal 2)
# Watch sensor demo output:
# [WARN] Connection lost, buffering messages...
# [INFO] Buffered 1 messages
# [INFO] Buffered 2 messages
# ...

# Restart gateway
cd src/NatsWebSocketBridge.Gateway && dotnet run

# Watch sensor demo reconnect and drain buffer:
# [INFO] Reconnected after 2 attempts
# [INFO] Draining 5 buffered messages...
# [INFO] Buffer drained successfully
```

---

## Demo 6: Subscribe to Commands

```bash
# Modify sensor_demo to add subscription
# (or run command_demo example)

./examples/command_demo

# Terminal 5: Send command via NATS
nats pub commands.SENSOR-001.calibrate '{"offset": 0.5}'

# Watch command_demo receive:
# [INFO] Received command: calibrate
# [INFO] Executing calibration with offset: 0.5
```

---

## Demo 7: Request/Response Pattern

```bash
# Terminal 5: Start a config service responder
nats reply services.config.get '{"sampling_rate": 500}' &

# Run request demo
./examples/request_demo

# Output:
# [INFO] Requesting config...
# [INFO] Response: sampling_rate = 500
```

---

## Demo 8: Metrics Output

```bash
# Run sensor demo with logging metrics
./examples/sensor_demo --metrics=logging

# Output shows metrics:
# [METRIC] onConnect: connected to gateway
# [METRIC] onMessageSent: telemetry.SENSOR-001.temp, 45 bytes
# [METRIC] onPublishLatency: 2ms
# [METRIC] onBufferSize: 0/1000

# With Prometheus metrics
./examples/sensor_demo --metrics=prometheus --metrics-port=9100

# Check metrics endpoint
curl http://localhost:9100/metrics | grep device_sdk
```

---

## Demo 9: Buffer Overflow Policies

```bash
# Run with small buffer to demonstrate overflow
./examples/buffer_demo --buffer-size=5

# Stop gateway to cause buffering
# (Ctrl+C in gateway terminal)

# Watch buffer fill and overflow:
# [INFO] Buffer: 1/5
# [INFO] Buffer: 2/5
# ...
# [INFO] Buffer: 5/5
# [WARN] Buffer overflow, dropping oldest message
# [METRIC] onBufferOverflow: dropped 1 messages
```

---

## Demo 10: Error Handling

```bash
# Test auth failure
./examples/sensor_demo --api-key=invalid

# Output:
# [ERROR] AuthenticationError: Invalid API key
# [INFO] Retrying connection...

# Test connection timeout
./examples/sensor_demo --server=ws://nonexistent:5000/ws

# Output:
# [ERROR] ConnectionError: Connection timed out after 10s
```

---

## Demo 11: TLS Configuration

```bash
# Run with TLS (requires certs)
./examples/sensor_demo \
  --server=wss://gateway:5443/ws \
  --ca-cert=/path/to/ca.crt \
  --client-cert=/path/to/client.crt \
  --client-key=/path/to/client.key

# Verify TLS connection in gateway logs
```

---

## Demo 12: Code Walkthrough - Client Implementation

```bash
# Show main client implementation
cat ../src/client.cpp | head -100

# Key methods:
# - connect(): WebSocket connection + auth
# - publish(): Send message, wait for ack
# - publishAsync(): Queue for send
# - processMessages(): Main loop
```

---

## Demo 13: Code Walkthrough - Buffer

```bash
# Show buffer implementation
cat ../src/buffer.cpp

# Ring buffer with thread-safe access
# Overflow policies
# FIFO ordering
```

---

## Demo 14: Integration Example

```cpp
// Real-world integration pattern
cat ../examples/production_sensor.cpp
```

```cpp
#include <gateway/client.h>
#include <gateway/metrics.h>
#include <sensors/temperature.h>

int main() {
    // Load config from environment
    auto opts = gateway::loadOptionsFromEnv();

    // Setup Prometheus metrics
    auto metrics = std::make_shared<gateway::PrometheusMetricsCallback>(
        "sensor_sdk", 9100);

    gateway::GatewayClient client(opts);
    client.setMetricsCallback(metrics);

    // Handle commands
    client.subscribe("commands." + opts.deviceId + ".>",
        [](const gateway::Message& msg) {
            handleCommand(msg);
        });

    client.connect();

    // Main telemetry loop
    sensors::TemperatureSensor sensor("/dev/temp0");
    while (running) {
        auto reading = sensor.read();
        client.publishAsync("telemetry." + opts.deviceId + ".temp", {
            {"value", reading.celsius},
            {"quality", reading.quality},
            {"timestamp", reading.timestamp}
        });
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    client.disconnect();
}
```

---

## Cleanup

```bash
# Stop sensor demos (Ctrl+C)

# Stop gateway (Ctrl+C)

# Stop NATS
docker stop nats && docker rm nats
```

---

## Troubleshooting

### Build fails with missing dependencies
```bash
# Install libwebsockets
brew install libwebsockets  # macOS
apt install libwebsockets-dev  # Ubuntu

# Install nlohmann/json
brew install nlohmann-json
apt install nlohmann-json3-dev
```

### Connection refused
```bash
# Check gateway is running
curl http://localhost:5000/health

# Check firewall
sudo ufw allow 5000
```

### Messages not received
```bash
# Verify subscription pattern
nats sub "commands.SENSOR-001.>"

# Check permissions in auth response
```
