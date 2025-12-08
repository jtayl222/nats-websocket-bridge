# Episode 05: Device SDK (C++)

**Duration:** 12-15 minutes
**Prerequisites:** [Episode 01](../01-intro/README.md) through [Episode 04](../04-websocket-protocol/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (5 of 7)

## Learning Objectives

By the end of this episode, viewers will understand:
- C++ SDK architecture for embedded packaging line controllers
- Async connection management with automatic reconnection
- Message buffering for offline operation (critical for factory floor reliability)
- Metrics callback integration for Prometheus observability

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

## Pharmaceutical Packaging Line Integration

The C++ SDK is designed for integration with packaging line equipment controllers:

| Equipment Type | Typical Controller | Integration Pattern |
|---------------|-------------------|---------------------|
| Blister Sealers | Beckhoff TwinCAT, Siemens S7 | PLC companion application |
| Cartoners | Rockwell CompactLogix | OPC UA to SDK bridge |
| Case Packers | FANUC controllers | Direct SDK integration |
| Vision Systems | Cognex, Keyence | Inspection result publisher |
| Serialization | Antares, TraceLink | Aggregation event publisher |

### Offline Buffering for ALCOA+ Compliance

The SDK's offline buffer ensures **contemporaneous** data capture even during network outages:

```cpp
// Configure buffer for 24-hour retention at 1 msg/sec = 86,400 messages
gateway::ConnectionOptions opts;
opts.bufferSize = 100000;  // Messages retained during disconnect
opts.bufferStrategy = BufferStrategy::FIFO;  // Oldest messages dropped if full

// All buffered messages include original capture timestamp
// Supports ALCOA+ "Contemporaneous" requirement
```

**Data Integrity:** Each message is timestamped at capture time (not send time), ensuring accurate batch records even after network recovery.

## Related Documentation

- [Episode 04: WebSocket Protocol](../04-websocket-protocol/README.md) - Protocol the SDK implements
- [Episode 06: Monitoring](../06-monitoring-observability/README.md) - SDK metrics integration
- [Historical Data Retention](../../../compliance/HISTORICAL_DATA_RETENTION.md) - How device data feeds the historian

## Next Episode

→ [Episode 06: Monitoring & Observability](../06-monitoring-observability/README.md) - Instrumenting the entire stack
