# Episode 03: Gateway Architecture

**Duration:** 15-18 minutes
**Prerequisites:** [Episode 01](../01-intro/README.md), [Episode 02](../02-nats-fundamentals/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (3 of 7)

## Learning Objectives

By the end of this episode, viewers will understand:
- Why a gateway sits between devices and NATS (protocol translation, security boundary)
- C# WebSocket handling with ASP.NET Core for production environments
- Connection management for thousands of concurrent packaging line devices
- Message routing and validation patterns for pharmaceutical telemetry

## Outline

1. **Why a Gateway?** (0:00-2:00)
   - Protocol translation (WebSocket → NATS)
   - Authentication boundary
   - Rate limiting and validation
   - Centralized observability

2. **Project Structure** (2:00-4:00)
   - Solution walkthrough
   - Key directories: Handlers, Services, Configuration
   - Dependency injection setup

3. **WebSocket Middleware** (4:00-7:00)
   - ASP.NET Core WebSocket support
   - Connection upgrade process
   - WebSocketMiddleware implementation
   - Code walkthrough

4. **Device Connection Manager** (7:00-10:00)
   - Tracking active connections
   - ConcurrentDictionary for thread safety
   - Connection lifecycle events
   - Metrics integration

5. **NATS Integration** (10:00-13:00)
   - JetStreamNatsService walkthrough
   - Publishing messages to streams
   - Consumer management
   - Error handling and retries

6. **Configuration** (13:00-15:00)
   - appsettings.json structure
   - Stream configuration
   - Consumer configuration
   - Options pattern in C#

7. **Running the Gateway** (15:00-17:00)
   - Docker Compose setup
   - Health checks
   - Startup sequence
   - Demo: Gateway logs

## Key Code Files

```
src/NatsWebSocketBridge.Gateway/
├── Program.cs                    # Service configuration
├── Handlers/
│   ├── WebSocketMiddleware.cs    # HTTP → WebSocket upgrade
│   └── DeviceWebSocketHandler.cs # Message handling
├── Services/
│   ├── DeviceConnectionManager.cs
│   ├── JetStreamNatsService.cs
│   └── GatewayMetrics.cs
└── Configuration/
    ├── GatewayOptions.cs
    └── JetStreamOptions.cs
```

## Demo

```bash
# Start infrastructure
docker-compose -f docker/monitoring/docker-compose.yml up -d nats

# Run gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Test with wscat
wscat -c ws://localhost:5000/ws
```

## Key Visuals

- Class diagram of services
- Request flow through middleware
- Connection state machine
- Configuration structure

## Gateway Role in Pharmaceutical Context

The gateway serves as the critical boundary between factory floor devices and enterprise systems:

```
┌─────────────────────────────────────────────────────────────────┐
│                    FACTORY FLOOR (OT Network)                    │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐            │
│  │ Blister │  │Cartoner │  │  Case   │  │ Serial- │            │
│  │ Sealer  │  │         │  │ Packer  │  │ ization │            │
│  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘            │
│       │            │            │            │                   │
│       └────────────┴────────────┴────────────┘                   │
│                           │                                      │
│                    WebSocket (WSS)                               │
└───────────────────────────┼──────────────────────────────────────┘
                            │
┌───────────────────────────┼──────────────────────────────────────┐
│                    ┌──────▼──────┐                               │
│                    │   GATEWAY   │  ← Episode 03 focus           │
│                    │             │                               │
│                    │ • Auth      │                               │
│                    │ • Validate  │                               │
│                    │ • Route     │                               │
│                    │ • Metrics   │                               │
│                    └──────┬──────┘                               │
│                           │                                      │
│                    ENTERPRISE (IT Network)                       │
└───────────────────────────┼──────────────────────────────────────┘
                            │
                      NATS + JetStream
```

**Key Responsibilities:**
- **Authentication**: Validates device credentials before allowing message flow
- **Authorization**: Enforces topic-level permissions per device type
- **Rate Limiting**: Prevents runaway devices from overwhelming the system
- **Audit Logging**: Records all authentication events for FDA compliance

## Related Documentation

- [Monitoring Architecture](../../../monitoring/MONITORING_ARCHITECTURE.md) - Gateway metrics and observability
- [Episode 04: WebSocket Protocol](../04-websocket-protocol/README.md) - Message format details
- [Episode 06: Monitoring](../06-monitoring-observability/README.md) - Instrumenting the gateway

## Next Episode

→ [Episode 04: WebSocket Protocol](../04-websocket-protocol/README.md) - Authentication flow and message formats
