# Episode 05: Device SDK (C++) - Slides

---

## Slide 1: Title

# Device SDK Deep Dive
## C++ Client for Embedded Systems

**NATS WebSocket Bridge Series - Episode 05**

---

## Slide 2: Episode Goals

### What You'll Learn

- C++ SDK architecture
- Connection lifecycle management
- Offline buffering strategy
- Metrics and observability

---

## Slide 3: Why C++ for Devices?

### Embedded System Constraints

| Constraint | C++ Solution |
|------------|--------------|
| Limited RAM | No garbage collector, manual memory |
| Real-time | Deterministic timing, no GC pauses |
| Low power | Efficient code, minimal overhead |
| Cross-platform | Compile anywhere |
| Legacy code | Easy integration |

**C++ gives you control when it matters**

---

## Slide 4: SDK Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Application Code                      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │ GatewayClient│  │  Message    │  │  Metrics   │     │
│  │   (main)    │  │   Buffer    │  │  Callback  │     │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘     │
│         │                │                │             │
│  ┌──────┴────────────────┴────────────────┴──────┐     │
│  │              Connection Manager                │     │
│  │         (WebSocket + Reconnection)            │     │
│  └───────────────────────┬───────────────────────┘     │
│                          │                              │
├──────────────────────────┼──────────────────────────────┤
│                          │                              │
│  ┌───────────────────────┴───────────────────────┐     │
│  │              libwebsockets                     │     │
│  │           (WebSocket library)                 │     │
│  └───────────────────────────────────────────────┘     │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 5: Project Structure

```
sdk/cpp/
├── include/gateway/
│   ├── client.h          # GatewayClient class
│   ├── options.h         # ConnectionOptions
│   ├── message.h         # Message, Payload types
│   ├── metrics.h         # MetricsCallback interface
│   └── buffer.h          # MessageBuffer class
├── src/
│   ├── client.cpp        # Implementation
│   ├── connection.cpp    # WebSocket handling
│   ├── buffer.cpp        # Ring buffer impl
│   └── metrics.cpp       # Built-in metrics
├── examples/
│   └── sensor_demo.cpp   # Complete example
├── tests/
│   └── ...
└── CMakeLists.txt        # Build configuration
```

---

## Slide 6: GatewayClient Interface

```cpp
namespace gateway {

class GatewayClient {
public:
    explicit GatewayClient(const ConnectionOptions& options);
    ~GatewayClient();

    // Lifecycle
    void connect();
    void disconnect();
    bool isConnected() const;

    // Publishing
    bool publish(const std::string& subject, const json& payload);
    bool publishAsync(const std::string& subject, const json& payload);

    // Subscribing
    void subscribe(const std::string& subject,
                   MessageCallback callback);
    void unsubscribe(const std::string& subject);

    // Request/Response
    std::optional<json> request(const std::string& subject,
                                 const json& payload,
                                 std::chrono::milliseconds timeout);

    // Observability
    void setMetricsCallback(std::shared_ptr<MetricsCallback> callback);

private:
    class Impl;
    std::unique_ptr<Impl> pImpl;
};

} // namespace gateway
```

---

## Slide 7: Connection Options

```cpp
struct ConnectionOptions {
    // Required
    std::string serverUrl;      // "ws://gateway:5000/ws"
    std::string deviceId;       // "SENSOR-001"
    std::string apiKey;         // "sk_live_..."

    // Reconnection
    bool autoReconnect = true;
    std::chrono::seconds initialReconnectDelay{1};
    std::chrono::seconds maxReconnectDelay{60};
    int maxReconnectAttempts = -1;  // -1 = unlimited

    // Buffering
    size_t bufferSize = 1000;
    BufferOverflowPolicy overflowPolicy = DropOldest;

    // Timeouts
    std::chrono::seconds connectTimeout{10};
    std::chrono::seconds pingInterval{30};
    std::chrono::seconds pingTimeout{10};

    // TLS (optional)
    std::string caCertPath;
    std::string clientCertPath;
    std::string clientKeyPath;
};
```

---

## Slide 8: Connection State Machine

```
     ┌─────────────────────────────────────────────┐
     │                                             │
     ▼                                             │
┌──────────┐                                       │
│DISCONNECTED│──────────────┐                      │
└──────────┘                │                      │
     │                      │ connect()            │
     │ autoReconnect        ▼                      │
     │              ┌──────────────┐               │
     │              │ CONNECTING   │               │
     │              └──────┬───────┘               │
     │                     │                       │
     │            ┌────────┼────────┐              │
     │            ▼        ▼        ▼              │
     │     ┌─────────┐ ┌───────┐ ┌───────┐        │
     │     │ TIMEOUT │ │AUTHING│ │ ERROR │        │
     │     └────┬────┘ └───┬───┘ └───┬───┘        │
     │          │          │         │             │
     │          └──────────┼─────────┘             │
     │                     │                       │
     │                     ▼                       │
     │              ┌──────────┐                   │
     └──────────────│CONNECTED │───────────────────┘
                    └──────────┘   disconnect()
                          │
                          │ error
                          ▼
                    ┌──────────┐
                    │RECONNECTING│
                    └──────────┘
```

---

## Slide 9: Basic Usage Example

```cpp
#include <gateway/client.h>
#include <iostream>

int main() {
    // Configure connection
    gateway::ConnectionOptions opts;
    opts.serverUrl = "ws://gateway:5000/ws";
    opts.deviceId = "SENSOR-001";
    opts.apiKey = "sk_live_abc123";
    opts.autoReconnect = true;

    // Create client
    gateway::GatewayClient client(opts);

    // Connect (blocks until authenticated)
    client.connect();

    // Publish telemetry
    client.publish("telemetry.SENSOR-001.temperature", {
        {"value", 23.5},
        {"unit", "C"},
        {"timestamp", getCurrentTimestamp()}
    });

    // Clean shutdown
    client.disconnect();
    return 0;
}
```

---

## Slide 10: Async Publishing

```cpp
// Async publish - returns immediately
// Message goes to buffer if offline
client.publishAsync("telemetry.sensor.temp", {
    {"value", readTemperature()}
});

// Sync publish - waits for NATS acknowledgment
bool success = client.publish("critical.alert", {
    {"level", "critical"},
    {"message", "Pressure exceeded threshold"}
});

if (!success) {
    // Handle publish failure
    log.error("Failed to publish critical alert");
}
```

**When to use each:**
- `publishAsync`: High-frequency telemetry, fire-and-forget
- `publish`: Critical messages, need confirmation

---

## Slide 11: Message Buffer

### Offline Operation Support

```cpp
class MessageBuffer {
public:
    MessageBuffer(size_t capacity, OverflowPolicy policy);

    void push(Message msg);
    std::optional<Message> pop();
    size_t size() const;
    bool empty() const;
    void clear();

private:
    std::deque<Message> buffer_;
    size_t capacity_;
    OverflowPolicy policy_;
    std::mutex mutex_;
};
```

**Overflow Policies:**
- `DropOldest` - Remove oldest, add new (default)
- `DropNewest` - Reject new messages when full
- `Block` - Wait for space (careful with real-time!)

---

## Slide 12: Buffer Behavior

```
Normal Operation (connected):
┌────────────────────────────────────────────┐
│  App ──publish──→ Client ──WebSocket──→ Gateway
└────────────────────────────────────────────┘

Offline Operation (disconnected):
┌────────────────────────────────────────────┐
│  App ──publish──→ Buffer                   │
│                   [msg1][msg2][msg3]...    │
└────────────────────────────────────────────┘

Reconnection (buffer drain):
┌────────────────────────────────────────────┐
│  Buffer ──drain──→ Client ──WebSocket──→ Gateway
│  [msg1][msg2][msg3] → sent in order        │
└────────────────────────────────────────────┘
```

---

## Slide 13: Subscribing to Commands

```cpp
// Subscribe with callback
client.subscribe("commands.SENSOR-001.>", [](const Message& msg) {
    std::cout << "Received command: " << msg.subject << std::endl;

    auto payload = msg.payload;

    if (msg.subject.ends_with(".calibrate")) {
        double offset = payload["offset"].get<double>();
        performCalibration(offset);
    }
    else if (msg.subject.ends_with(".restart")) {
        bool force = payload.value("force", false);
        initiateRestart(force);
    }
    else if (msg.subject.ends_with(".config")) {
        updateConfiguration(payload);
    }
});

// Unsubscribe when done
client.unsubscribe("commands.SENSOR-001.>");
```

---

## Slide 14: Request/Response

```cpp
// Synchronous request with timeout
auto response = client.request(
    "services.config.get",
    {{"key", "sampling_rate"}},
    std::chrono::seconds(5)
);

if (response.has_value()) {
    int rate = (*response)["sampling_rate"].get<int>();
    setSamplingRate(rate);
} else {
    // Timeout or error
    log.warn("Config request timed out, using default");
    setSamplingRate(DEFAULT_RATE);
}
```

---

## Slide 15: Metrics Callback

### Observability Interface

```cpp
class MetricsCallback {
public:
    virtual ~MetricsCallback() = default;

    // Connection events
    virtual void onConnect() = 0;
    virtual void onDisconnect(const std::string& reason) = 0;
    virtual void onReconnect(int attempt) = 0;

    // Message metrics
    virtual void onMessageSent(const std::string& subject,
                               size_t bytes) = 0;
    virtual void onMessageReceived(const std::string& subject,
                                   size_t bytes) = 0;

    // Buffer metrics
    virtual void onBufferSize(size_t size, size_t capacity) = 0;
    virtual void onBufferOverflow(size_t dropped) = 0;

    // Latency
    virtual void onPublishLatency(std::chrono::microseconds latency) = 0;
};
```

---

## Slide 16: Built-in Metrics Implementations

```cpp
// Logging metrics (for development)
auto loggingMetrics = std::make_shared<LoggingMetricsCallback>();
client.setMetricsCallback(loggingMetrics);

// Prometheus metrics (for production)
auto promMetrics = std::make_shared<PrometheusMetricsCallback>(
    "device_sdk",  // metric prefix
    9100           // metrics port
);
client.setMetricsCallback(promMetrics);

// Custom implementation
class MyMetrics : public MetricsCallback {
    void onMessageSent(const std::string& subject, size_t bytes) override {
        // Send to custom monitoring system
        statsd.increment("messages.sent");
        statsd.gauge("bytes.sent", bytes);
    }
    // ... implement other methods
};
```

---

## Slide 17: Error Handling

```cpp
try {
    client.connect();
}
catch (const ConnectionError& e) {
    log.error("Connection failed: {}", e.what());
    // Retry logic or fallback
}
catch (const AuthenticationError& e) {
    log.error("Auth failed: {}", e.what());
    // Check credentials
}

// Or use callbacks for async errors
client.setErrorCallback([](const Error& error) {
    switch (error.code) {
        case ErrorCode::ConnectionLost:
            // Will auto-reconnect if enabled
            break;
        case ErrorCode::AuthFailed:
            // Credentials invalid, need intervention
            break;
        case ErrorCode::RateLimited:
            // Slow down publishing
            break;
    }
});
```

---

## Slide 18: Thread Safety

### SDK Threading Model

```
┌─────────────────────────────────────────────────────────┐
│                    User Thread                           │
│  ┌─────────────────────────────────────────────────┐    │
│  │  client.publish()    client.subscribe()         │    │
│  └───────────────────────┬─────────────────────────┘    │
│                          │                              │
├──────────────────────────┼──────────────────────────────┤
│                          │ (thread-safe queue)          │
├──────────────────────────┼──────────────────────────────┤
│                          ▼                              │
│                    I/O Thread                           │
│  ┌─────────────────────────────────────────────────┐    │
│  │  WebSocket send/receive    Reconnection logic   │    │
│  └─────────────────────────────────────────────────┘    │
│                          │                              │
├──────────────────────────┼──────────────────────────────┤
│                          ▼                              │
│                 Callback Thread                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │  Message callbacks    Metrics callbacks         │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

**Safe from any thread:** `publish()`, `publishAsync()`, `request()`

---

## Slide 19: Building the SDK

```cmake
# CMakeLists.txt
cmake_minimum_required(VERSION 3.16)
project(gateway-sdk VERSION 1.0.0)

set(CMAKE_CXX_STANDARD 17)

# Dependencies
find_package(libwebsockets REQUIRED)
find_package(nlohmann_json REQUIRED)

# Library
add_library(gateway-sdk
    src/client.cpp
    src/connection.cpp
    src/buffer.cpp
    src/metrics.cpp
)

target_include_directories(gateway-sdk PUBLIC include)
target_link_libraries(gateway-sdk
    websockets
    nlohmann_json::nlohmann_json
)

# Example
add_executable(sensor_demo examples/sensor_demo.cpp)
target_link_libraries(sensor_demo gateway-sdk)
```

---

## Slide 20: Next Episode Preview

# Episode 06: Monitoring & Observability

- Prometheus metrics collection
- Grafana dashboards
- Loki log aggregation
- Alerting strategies

**See you in the next episode!**
