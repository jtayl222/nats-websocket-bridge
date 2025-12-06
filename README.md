# NATS WebSocket Bridge

A C# WebSocket to NATS gateway server for manufacturing IoT scenarios. This bridge enables devices to communicate via WebSocket while the gateway handles translation to NATS publish/subscribe operations.

## Features

- **Device Connection Management**: Maintains WebSocket connections from IoT devices
- **NATS Integration**: Translates WebSocket messages to NATS publish/subscribe operations with JetStream support
- **Device Authentication**: Authenticates devices before allowing message operations
- **Authorization Enforcement**: Controls what subjects each device can publish to or subscribe from
- **Message Validation**: Validates message format and content
- **Rate Limiting**: Token bucket-based throttling per device
- **Message Buffering**: Buffers outgoing messages per device connection
- **NATS Topology Abstraction**: Hides NATS infrastructure details from devices

## Architecture

```
Devices ─Socket/WebSocket─> Gateway Server ─NATS client─> JetStream Consumer
```

## Getting Started

### Prerequisites

- .NET 10.0 or later
- NATS Server with JetStream enabled

### Configuration

Configure the gateway in `appsettings.json`:

```json
{
  "Gateway": {
    "MaxMessageSize": 1048576,
    "MessageRateLimitPerSecond": 100,
    "OutgoingBufferSize": 1000,
    "AuthenticationTimeoutSeconds": 30,
    "PingIntervalSeconds": 30,
    "PingTimeoutSeconds": 10
  },
  "Nats": {
    "Url": "nats://localhost:4222",
    "ClientName": "NatsWebSocketBridge",
    "StreamName": "DEVICES",
    "UseJetStream": true,
    "ConnectionTimeoutSeconds": 10,
    "ReconnectDelayMs": 1000,
    "MaxReconnectAttempts": -1
  }
}
```

### Running the Gateway

```bash
cd src/NatsWebSocketBridge.Gateway
dotnet run
```

The gateway will start on `http://localhost:5000` (or configured port).

## WebSocket Protocol

### Authentication

Devices must authenticate immediately after connecting:

```json
{
  "type": 8,
  "payload": {
    "deviceId": "sensor-temp-001",
    "token": "temp-sensor-token-001",
    "deviceType": "sensor"
  }
}
```

### Message Types

| Type | Name        | Description                           |
|------|-------------|---------------------------------------|
| 0    | Publish     | Publish a message to a NATS subject   |
| 1    | Subscribe   | Subscribe to a NATS subject           |
| 2    | Unsubscribe | Unsubscribe from a NATS subject       |
| 3    | Message     | Message received from a subscription  |
| 4    | Request     | Request/reply message                 |
| 5    | Reply       | Reply to a request                    |
| 6    | Ack         | Acknowledgment message                |
| 7    | Error       | Error message                         |
| 8    | Auth        | Authentication message                |
| 9    | Ping        | Ping/keepalive message                |
| 10   | Pong        | Pong response to ping                 |

### Publishing Messages

```json
{
  "type": 0,
  "subject": "devices.sensor-temp-001.data",
  "payload": {
    "temperature": 25.5,
    "unit": "celsius"
  },
  "correlationId": "msg-123"
}
```

### Subscribing to Subjects

```json
{
  "type": 1,
  "subject": "devices.sensor-temp-001.commands"
}
```

### Receiving Messages

```json
{
  "type": 3,
  "subject": "devices.sensor-temp-001.commands",
  "payload": {
    "command": "calibrate"
  },
  "timestamp": "2025-01-01T00:00:00Z"
}
```

## API Endpoints

| Endpoint   | Method | Description                     |
|------------|--------|---------------------------------|
| `/ws`      | WS     | WebSocket endpoint for devices  |
| `/health`  | GET    | Health check endpoint           |
| `/devices` | GET    | List connected devices          |

## Subject Authorization

Devices are authorized based on topic patterns using NATS-style wildcards:

- `*` matches a single token
- `>` matches one or more tokens (must be at end)

Example device permissions:
```json
{
  "allowedPublishTopics": ["devices.sensor-temp-001.data", "devices.sensors.temperature"],
  "allowedSubscribeTopics": ["devices.sensor-temp-001.commands", "devices.broadcast"]
}
```

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Project Structure

```
├── src/
│   └── NatsWebSocketBridge.Gateway/
│       ├── Auth/                      # Authentication & authorization
│       ├── Configuration/             # Configuration options
│       ├── Handlers/                  # WebSocket handlers
│       ├── Models/                    # Data models
│       ├── Services/                  # Core services
│       └── Program.cs                 # Application entry point
└── tests/
    └── NatsWebSocketBridge.Tests/     # Unit tests
```

## License

MIT License - see LICENSE file for details.