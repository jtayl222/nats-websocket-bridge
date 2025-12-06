# Gateway Device SDK for C++

A C++ SDK for connecting industrial devices to the NATS WebSocket Bridge Gateway.

## Features

- Simple, clean API for device manufacturers
- WebSocket transport with TLS support
- Automatic authentication and authorization
- Automatic reconnection with exponential backoff
- Heartbeat/keep-alive management
- Publish/subscribe messaging
- Thread-safe design
- Configurable logging

## Quick Start

```cpp
#include <gateway/gateway_device.h>

int main() {
    // Configure
    gateway::GatewayConfig config;
    config.gatewayUrl = "wss://gateway.example.com/ws";
    config.deviceId = "sensor-001";
    config.authToken = "your-device-token";
    config.deviceType = gateway::DeviceType::Sensor;

    // Create client
    gateway::GatewayClient client(config);

    // Connect
    if (!client.connect()) {
        return 1;
    }

    // Subscribe to commands
    client.subscribe("commands.>", [](const std::string& subject,
                                       const gateway::JsonValue& payload,
                                       const gateway::Message& msg) {
        std::cout << "Command: " << subject << std::endl;
    });

    // Publish sensor data
    gateway::JsonValue data = gateway::JsonValue::object();
    data["temperature"] = 25.5;
    client.publish("sensors.temperature", data);

    // Run event loop
    while (client.isConnected()) {
        client.poll();
    }

    return 0;
}
```

## Building

### Prerequisites

- C++17 compatible compiler
- CMake 3.14+
- OpenSSL development libraries
- Optional: libwebsockets (will be fetched if not found)

### Build Steps

```bash
mkdir build && cd build
cmake ..
cmake --build .
```

### Build Options

| Option | Default | Description |
|--------|---------|-------------|
| `BUILD_EXAMPLES` | ON | Build example applications |
| `BUILD_TESTS` | OFF | Build unit tests |
| `BUILD_SHARED_LIBS` | OFF | Build as shared library |

```bash
# Example: Build with tests
cmake -DBUILD_TESTS=ON ..
```

## Configuration

### GatewayConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `gatewayUrl` | string | Yes | WebSocket URL (ws:// or wss://) |
| `deviceId` | string | Yes | Unique device identifier |
| `authToken` | string | Yes | Authentication token |
| `deviceType` | DeviceType | No | sensor, actuator, controller, gateway, custom |
| `connectTimeout` | Duration | No | Connection timeout (default: 10s) |
| `authTimeout` | Duration | No | Authentication timeout (default: 30s) |

### Using ConfigBuilder

```cpp
auto config = gateway::GatewayConfigBuilder()
    .gatewayUrl("wss://gateway.example.com/ws")
    .deviceId("sensor-001")
    .authToken("token123")
    .deviceType(gateway::DeviceType::Sensor)
    .enableReconnect(true, Duration{1000}, Duration{60000})
    .enableHeartbeat(Duration{30000})
    .build();
```

### TLS Configuration

```cpp
config.tls.enabled = true;
config.tls.verifyPeer = true;  // Set to false only for development
config.tls.caCertPath = "/path/to/ca.pem";
config.tls.clientCertPath = "/path/to/client.pem";  // For mutual TLS
config.tls.clientKeyPath = "/path/to/client.key";
```

### Reconnection Configuration

```cpp
config.reconnect.enabled = true;
config.reconnect.initialDelay = Duration{1000};    // 1 second
config.reconnect.maxDelay = Duration{60000};       // 1 minute
config.reconnect.backoffMultiplier = 2.0;
config.reconnect.maxAttempts = 0;                  // 0 = unlimited
config.reconnect.resubscribeOnReconnect = true;
```

## API Reference

### Connection

```cpp
// Blocking connect
bool connect();

// Non-blocking connect
Result<void> connectAsync();

// Disconnect
void disconnect();

// Check connection status
bool isConnected();
ConnectionState getState();
```

### Publishing

```cpp
// Publish JSON payload
Result<void> publish(const std::string& subject, const JsonValue& payload);

// Publish string payload
Result<void> publish(const std::string& subject, const std::string& payload);

// Publish with QoS
Result<void> publish(const std::string& subject, const JsonValue& payload, QoS qos);
```

### Subscribing

```cpp
// Subscribe with handler
Result<SubscriptionId> subscribe(const std::string& subject, SubscriptionHandler handler);

// Unsubscribe
Result<void> unsubscribe(SubscriptionId id);
Result<void> unsubscribe(const std::string& subject);

// Get active subscriptions
std::vector<std::string> getSubscriptions();
```

### Event Loop

```cpp
// Process events (call regularly)
void poll(Duration timeout = Duration{100});

// Blocking run
void run();

// Background run
bool runAsync();
void stop();
```

### Callbacks

```cpp
client.onConnected([] {
    std::cout << "Connected!" << std::endl;
});

client.onDisconnected([](ErrorCode code, const std::string& reason) {
    std::cout << "Disconnected: " << reason << std::endl;
});

client.onError([](ErrorCode code, const std::string& message) {
    std::cerr << "Error: " << message << std::endl;
});

client.onReconnecting([](uint32_t attempt) {
    std::cout << "Reconnecting (attempt " << attempt << ")" << std::endl;
});
```

## Message Format

Messages use JSON format compatible with the gateway protocol:

```json
{
  "type": 0,
  "subject": "sensors.temperature",
  "payload": {
    "value": 25.5,
    "unit": "celsius"
  },
  "timestamp": "2024-01-15T10:30:00.000Z"
}
```

### Message Types

| Type | Value | Description |
|------|-------|-------------|
| Publish | 0 | Publish message to subject |
| Subscribe | 1 | Subscribe to subject |
| Unsubscribe | 2 | Unsubscribe from subject |
| Message | 3 | Received message |
| Request | 4 | Request/reply pattern |
| Reply | 5 | Reply to request |
| Ack | 6 | Acknowledgment |
| Error | 7 | Error message |
| Auth | 8 | Authentication |
| Ping | 9 | Keep-alive ping |
| Pong | 10 | Keep-alive pong |

## Subject Naming

Follow NATS subject conventions:

- Use dots for hierarchy: `factory.line1.sensor.temperature`
- Use `*` for single-token wildcard: `factory.*.sensor`
- Use `>` for multi-token wildcard: `factory.>`

Recommended patterns:

| Pattern | Use Case |
|---------|----------|
| `telemetry.{deviceId}.{metric}` | Sensor data |
| `commands.{deviceId}.{action}` | Device commands |
| `status.{deviceId}` | Device status |
| `alerts.{deviceId}.{type}` | Alerts/alarms |
| `config.{deviceId}` | Configuration |

## Error Handling

```cpp
auto result = client.publish("topic", data);
if (result.failed()) {
    std::cerr << "Error: " << result.errorMessage()
              << " (code: " << errorCodeToString(result.error()) << ")"
              << std::endl;
}
```

### Error Codes

| Category | Range | Examples |
|----------|-------|----------|
| Connection | 100-199 | ConnectionFailed, ConnectionTimeout |
| Authentication | 200-299 | AuthenticationFailed, InvalidCredentials |
| Authorization | 300-399 | NotAuthorized, TopicNotAllowed |
| Protocol | 400-499 | InvalidMessage, PayloadTooLarge |
| Operation | 500-599 | NotConnected, RateLimitExceeded |

## Examples

See the `examples/` directory:

- `simple_sensor.cpp` - Minimal sensor example
- `temperature_sensor.cpp` - Production-ready sensor with error handling
- `actuator.cpp` - Bidirectional actuator control
- `controller.cpp` - PLC-style controller aggregating multiple devices

## Thread Safety

- All public GatewayClient methods are thread-safe
- Callbacks are invoked from the polling thread
- Use `runAsync()` for background event processing

## Logging

```cpp
// Use custom logger
auto logger = std::make_shared<gateway::CustomLogger>([](const LogEntry& entry) {
    // Your logging implementation
});
gateway::GatewayClient client(config, logger);

// Set log level
client.getLogger().setLevel(LogLevel::Debug);
```

## Integration Guide

1. Add SDK to your project (CMake FetchContent or manual)
2. Include `<gateway/gateway_device.h>`
3. Link against `gateway_device`
4. Configure with your gateway URL and credentials
5. Implement your device logic

## License

[Your License Here]
