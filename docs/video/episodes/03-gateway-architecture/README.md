# Episode 03: Gateway Architecture

**Duration:** 15-18 minutes
**Prerequisites:** Episodes 01-02

## Learning Objectives

By the end of this episode, viewers will understand:
- Why a gateway sits between devices and NATS
- C# WebSocket handling with ASP.NET Core
- Connection management for thousands of clients
- Message routing and validation patterns

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
