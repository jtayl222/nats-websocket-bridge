# Device Integration Guide

This guide walks device manufacturers through integrating the Gateway Device SDK.

## Prerequisites

- C++17 compatible compiler (GCC 7+, Clang 5+, MSVC 2017+)
- CMake 3.14 or higher
- OpenSSL development libraries
- Network access to the gateway

## Step 1: Add SDK to Your Project

### Option A: CMake FetchContent

```cmake
include(FetchContent)
FetchContent_Declare(
    gateway_device_sdk
    GIT_REPOSITORY https://github.com/your-org/gateway-device-sdk.git
    GIT_TAG v1.0.0
)
FetchContent_MakeAvailable(gateway_device_sdk)

target_link_libraries(your_app PRIVATE gateway_device)
```

### Option B: As Subdirectory

1. Copy SDK to `lib/gateway-sdk/`
2. Add to CMakeLists.txt:

```cmake
add_subdirectory(lib/gateway-sdk)
target_link_libraries(your_app PRIVATE gateway_device)
```

### Option C: Pre-built Library

1. Get prebuilt `libgateway_device.a` and headers
2. Add to your build:

```cmake
find_library(GATEWAY_SDK gateway_device PATHS /path/to/sdk/lib)
target_link_libraries(your_app ${GATEWAY_SDK})
target_include_directories(your_app PRIVATE /path/to/sdk/include)
```

## Step 2: Obtain Credentials

Contact your gateway administrator to receive:

1. **Gateway URL**: `wss://gateway.example.com/ws`
2. **Device ID**: Unique identifier (e.g., `sensor-temp-001`)
3. **Auth Token**: API key or authentication token
4. **Device Type**: `sensor`, `actuator`, `controller`, etc.
5. **Allowed Topics**: Subjects you can publish/subscribe to

## Step 3: Basic Integration

```cpp
#include <gateway/gateway_device.h>

// 1. Configure
gateway::GatewayConfig config;
config.gatewayUrl = "wss://gateway.example.com/ws";
config.deviceId = "your-device-id";
config.authToken = "your-auth-token";
config.deviceType = gateway::DeviceType::Sensor;

// 2. Create client
gateway::GatewayClient client(config);

// 3. Connect
if (!client.connect()) {
    // Handle connection failure
    return;
}

// 4. Use the client
// ...

// 5. Disconnect when done
client.disconnect();
```

## Step 4: Publishing Data

```cpp
// Create payload
gateway::JsonValue data = gateway::JsonValue::object();
data["temperature"] = readTemperature();
data["humidity"] = readHumidity();
data["timestamp_ms"] = getCurrentTimeMs();

// Publish
auto result = client.publish("telemetry.your-device.readings", data);
if (result.failed()) {
    handleError(result.error(), result.errorMessage());
}
```

## Step 5: Subscribing to Commands

```cpp
// Subscribe to commands for this device
client.subscribe("commands.your-device.>",
    [](const std::string& subject,
       const gateway::JsonValue& payload,
       const gateway::Message& msg) {

        // Extract action from payload
        if (payload.contains("action")) {
            std::string action = payload["action"].asString();

            if (action == "restart") {
                handleRestart();
            } else if (action == "configure") {
                handleConfigure(payload);
            }
        }
    });
```

## Step 6: Event Loop

Choose one of these patterns:

### Pattern A: Manual Polling (Recommended)

```cpp
while (shouldRun) {
    // Your device logic
    readSensors();
    publishData();

    // Process SDK events
    client.poll(gateway::Duration{100});
}
```

### Pattern B: Background Thread

```cpp
// Start SDK in background
client.runAsync();

// Your device logic runs normally
while (shouldRun) {
    readSensors();
    publishData();
    sleep(1);
}

// Stop background processing
client.stop();
```

### Pattern C: Blocking Run

```cpp
// This blocks until disconnect
client.run();
```

## Step 7: Handle Connection Events

```cpp
client.onConnected([] {
    log("Connected to gateway");
    publishStatus("online");
});

client.onDisconnected([](gateway::ErrorCode code, const std::string& reason) {
    log("Disconnected: " + reason);
    // SDK will automatically reconnect if enabled
});

client.onReconnecting([](uint32_t attempt) {
    log("Reconnecting, attempt " + std::to_string(attempt));
});

client.onError([](gateway::ErrorCode code, const std::string& message) {
    log("Error: " + message);
});
```

## Step 8: Graceful Shutdown

```cpp
// Handle SIGTERM/SIGINT
signal(SIGTERM, [](int) { shouldRun = false; });

// In your shutdown code:
void shutdown() {
    // Publish offline status
    gateway::JsonValue status = gateway::JsonValue::object();
    status["online"] = false;
    client.publish("status.your-device", status);

    // Allow message to be sent
    client.poll(gateway::Duration{200});

    // Disconnect
    client.disconnect();
}
```

## Configuration Best Practices

### Load from Environment

```cpp
gateway::GatewayConfig loadConfig() {
    gateway::GatewayConfig config;

    config.gatewayUrl = getenv("GATEWAY_URL") ?: "wss://localhost:5000/ws";
    config.deviceId = getenv("DEVICE_ID") ?: "device-001";
    config.authToken = getenv("DEVICE_TOKEN") ?: "";

    // Fail if no token
    if (config.authToken.empty()) {
        throw std::runtime_error("DEVICE_TOKEN environment variable required");
    }

    return config;
}
```

### Load from JSON File

```cpp
gateway::GatewayConfig loadConfigFromFile(const std::string& path) {
    std::ifstream file(path);
    nlohmann::json j;
    file >> j;

    gateway::GatewayConfig config;
    config.gatewayUrl = j["gateway_url"];
    config.deviceId = j["device_id"];
    config.authToken = j["auth_token"];
    config.deviceType = gateway::deviceTypeFromString(j["device_type"]);

    return config;
}
```

## Subject Naming Convention

Follow this pattern for consistent topic organization:

| Subject Pattern | Purpose |
|-----------------|---------|
| `telemetry.{deviceId}.{metric}` | Sensor readings |
| `commands.{deviceId}.{action}` | Device commands |
| `status.{deviceId}` | Online/offline status |
| `alerts.{deviceId}.{severity}` | Alarms and alerts |
| `config.{deviceId}` | Configuration updates |
| `heartbeat.{deviceId}` | Keep-alive heartbeats |

## Error Handling Patterns

### Check Every Result

```cpp
auto result = client.publish("topic", data);
if (result.failed()) {
    switch (result.error()) {
        case gateway::ErrorCode::NotConnected:
            // Queue for retry
            break;
        case gateway::ErrorCode::RateLimitExceeded:
            // Back off
            break;
        default:
            // Log and continue
            break;
    }
}
```

### Use Exceptions

```cpp
try {
    auto result = client.publish("topic", data);
    if (result.failed()) {
        throw gateway::GatewayException(result.error(), result.errorMessage());
    }
} catch (const gateway::GatewayException& e) {
    handleError(e);
}
```

## Embedded Systems Considerations

### Memory Constraints

```cpp
// Limit buffer sizes
config.buffer.maxOutgoingMessages = 100;
config.buffer.maxIncomingMessages = 100;
config.buffer.maxPayloadSize = 65536;  // 64KB
```

### Network Constraints

```cpp
// Reduce heartbeat frequency
config.heartbeat.interval = gateway::Duration{60000};

// Aggressive reconnection
config.reconnect.initialDelay = gateway::Duration{5000};
config.reconnect.maxDelay = gateway::Duration{300000};  // 5 minutes
```

### Power Constraints

```cpp
// Disable heartbeat when not needed
config.heartbeat.enabled = false;

// Connect only when needed
client.connect();
publishBatchData();
client.disconnect();
```

## Testing Your Integration

### Local Testing

1. Run local gateway instance
2. Set `GATEWAY_INSECURE=true` to skip TLS verification
3. Use test credentials

### Logging

```cpp
// Enable debug logging
config.logging.level = 1;  // Debug
config.logging.timestamps = true;

// Or use custom logger
auto logger = std::make_shared<gateway::CustomLogger>([](const LogEntry& e) {
    yourLoggingSystem.log(e.level, e.message);
});
gateway::GatewayClient client(config, logger);
```

### Monitoring

```cpp
// Get statistics
auto stats = client.getStats();
log("Messages sent: " + std::to_string(stats.messagesSent));
log("Messages received: " + std::to_string(stats.messagesReceived));
log("Errors: " + std::to_string(stats.errorCount));
log("Reconnects: " + std::to_string(stats.reconnectCount));
```

## Troubleshooting

### Connection Fails

1. Check URL format (must be `ws://` or `wss://`)
2. Verify network connectivity
3. Check TLS settings (verifyPeer)
4. Look for DNS resolution issues

### Authentication Fails

1. Verify device ID matches registered device
2. Check token hasn't expired
3. Verify device type matches configuration
4. Check gateway logs for details

### Messages Not Received

1. Verify subscription pattern matches published subjects
2. Check authorization for subscribe topics
3. Ensure poll() is being called regularly
4. Check for wildcard syntax errors

### High Latency

1. Reduce message size
2. Increase buffer sizes if queuing
3. Check network conditions
4. Consider message batching

## Support

For issues:
1. Check this guide and README
2. Review protocol specification
3. Enable debug logging
4. Contact gateway administrator
