# Episode 05: Device SDK (C++)

**Duration:** 12-15 minutes
**Prerequisites:** Episodes 01-04

## Learning Objectives

By the end of this episode, viewers will understand:
- C++ SDK architecture for embedded systems
- Async connection management
- Message buffering for offline operation
- Metrics callback integration

## Outline

1. **Why C++ for Devices** (0:00-1:30)
   - Embedded system constraints
   - Memory efficiency
   - Real-time requirements
   - Cross-platform compatibility

2. **SDK Architecture** (1:30-4:00)
   - GatewayClient class
   - Connection options
   - Async pattern with callbacks
   - Code structure walkthrough

3. **Connection Management** (4:00-7:00)
   - WebSocket establishment
   - Authentication handshake
   - Automatic reconnection
   - Exponential backoff
   - Demo: Connection lifecycle

4. **Publishing Messages** (7:00-9:00)
   - Sync vs async publish
   - Message buffering when offline
   - Buffer overflow handling
   - Demo: Publish telemetry

5. **Subscriptions** (9:00-11:00)
   - Subscribe to commands
   - Callback handlers
   - Message replay on reconnect
   - Demo: Receive commands

6. **Metrics & Observability** (11:00-13:00)
   - MetricsCallback interface
   - Built-in implementations
   - Integration with Prometheus
   - Demo: Metrics output

7. **Building & Integration** (13:00-14:00)
   - CMake setup
   - Dependencies (libwebsockets, nlohmann/json)
   - Example: Packaging line sensor

## Key Code Files

```
sdk/cpp/
├── include/gateway/
│   ├── client.h        # GatewayClient interface
│   ├── options.h       # Configuration options
│   ├── message.h       # Message types
│   └── metrics.h       # Metrics callback
├── src/
│   ├── client.cpp      # Implementation
│   └── metrics.cpp     # Built-in metrics
└── examples/
    └── sensor_demo.cpp # Integration example
```

## Demo Code

```cpp
#include <gateway/client.h>
#include <gateway/metrics.h>

int main() {
    gateway::ConnectionOptions opts;
    opts.serverUrl = "ws://gateway:5000/ws";
    opts.deviceId = "SENSOR-001";
    opts.apiKey = "sk_live_...";
    opts.autoReconnect = true;
    opts.bufferSize = 1000;

    auto metrics = std::make_shared<gateway::LoggingMetricsCallback>();

    gateway::GatewayClient client(opts);
    client.setMetricsCallback(metrics);

    client.connect();

    // Publish telemetry
    while (running) {
        double temp = readTemperature();
        client.publish("factory.line1.sensor.temp",
            {{"value", temp}, {"unit", "C"}});
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    client.disconnect();
    return 0;
}
```

## Key Visuals

- SDK class diagram
- Connection state machine
- Buffer behavior during disconnect
- Metrics flow diagram
