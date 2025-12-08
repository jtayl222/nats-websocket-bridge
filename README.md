# NATS WebSocket Bridge for Pharmaceutical Manufacturing

A production-grade C# WebSocket to NATS gateway designed for pharmaceutical packaging line connectivity. This bridge enables packaging equipment (blister sealers, cartoners, case packers, serialization systems) to communicate via WebSocket while the gateway handles translation to NATS publish/subscribe operations with FDA 21 CFR Part 11 compliance support.

## Pharmaceutical Manufacturing Context

This system addresses the unique challenges of pharmaceutical packaging environments:

| Challenge | Solution |
|-----------|----------|
| **FDA 21 CFR Part 11 Compliance** | Audit trails, data integrity checksums, immutable logs |
| **ALCOA+ Data Integrity** | Attributable, contemporaneous capture with device timestamps |
| **Batch Traceability** | Every message associated with batch_id for complete batch records |
| **Network Unreliability** | Offline buffering in Device SDK, automatic reconnection |
| **Real-time OEE Monitoring** | Sub-second message delivery for availability, performance, quality metrics |

## Features

- **Packaging Line Device Management**: Maintains WebSocket connections from PLCs, sensors, vision systems, and serialization equipment
- **NATS JetStream Integration**: Durable message storage with configurable retention for compliance
- **Device Authentication**: Token-based authentication for packaging line equipment
- **Authorization Enforcement**: Topic-level permissions per device type (sensors, actuators, controllers)
- **Message Validation**: Validates telemetry format and content
- **Rate Limiting**: Token bucket-based throttling per device
- **Message Buffering**: Buffers outgoing messages per device connection
- **Compliance-Ready Logging**: Structured JSON logs with batch_id, device_id for audit trails

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PHARMACEUTICAL PACKAGING LINE                             │
│                                                                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │
│  │   Blister   │  │  Cartoner   │  │    Case     │  │ Serializa-  │        │
│  │   Sealer    │  │             │  │   Packer    │  │    tion     │        │
│  │             │  │             │  │             │  │             │        │
│  │ Temperature │  │ Photo-eyes  │  │   Weight    │  │   Vision    │        │
│  │  Pressure   │  │   Counts    │  │   Scales    │  │   System    │        │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘        │
│         │                │                │                │                │
│         └────────────────┴────────────────┴────────────────┘                │
│                                   │                                         │
│                          WebSocket (WSS)                                    │
└───────────────────────────────────┼─────────────────────────────────────────┘
                                    │
                             ┌──────▼──────┐
                             │   GATEWAY   │
                             │             │
                             │ • Auth      │
                             │ • Validate  │
                             │ • Route     │
                             │ • Metrics   │
                             └──────┬──────┘
                                    │
                             ┌──────▼──────┐
                             │    NATS     │
                             │  JetStream  │
                             │             │
                             │ • TELEMETRY │
                             │ • EVENTS    │
                             │ • ALERTS    │
                             │ • QUALITY   │
                             │ • BATCHES   │
                             └──────┬──────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
             ┌──────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
             │ Prometheus  │ │ TimescaleDB │ │    Loki     │
             │  Metrics    │ │  Historian  │ │    Logs     │
             └─────────────┘ └─────────────┘ └─────────────┘
```

## Video Series

A comprehensive 7-episode video series covers the complete system:

| Episode | Title | Focus |
|---------|-------|-------|
| [01](docs/video/episodes/01-intro/README.md) | Introduction & Problem Space | Pharmaceutical manufacturing challenges |
| [02](docs/video/episodes/02-nats-fundamentals/README.md) | NATS Fundamentals | JetStream, subjects, consumers |
| [03](docs/video/episodes/03-gateway-architecture/README.md) | Gateway Architecture | C# WebSocket gateway design |
| [04](docs/video/episodes/04-websocket-protocol/README.md) | WebSocket Protocol | Authentication, message flow |
| [05](docs/video/episodes/05-device-sdk/README.md) | Device SDK | C++ SDK for embedded systems |
| [06](docs/video/episodes/06-monitoring-observability/README.md) | Monitoring & Observability | Prometheus, Grafana, Loki |
| [07](docs/video/episodes/07-historical-retention/README.md) | Historical Data Retention | FDA compliance, TimescaleDB |

See [Video Series Overview](docs/video/SERIES_OVERVIEW.md) for learning paths by role.

## Getting Started

### Prerequisites

- .NET 8.0 or later
- NATS Server with JetStream enabled
- Docker (for monitoring stack)

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
    "ClientName": "PharmaMfgGateway",
    "UseJetStream": true
  },
  "JetStream": {
    "Enabled": true,
    "Streams": [
      {
        "Name": "TELEMETRY",
        "Subjects": ["factory.>", "telemetry.>"],
        "MaxAge": "7d"
      },
      {
        "Name": "ALERTS",
        "Subjects": ["alerts.>"],
        "MaxAge": "30d",
        "DenyDelete": true
      }
    ]
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

Packaging line devices must authenticate immediately after connecting:

```json
{
  "type": 8,
  "payload": {
    "deviceId": "blister-sealer-line1-01",
    "token": "sealer-token-001"
  }
}
```

### Message Types

| Type | Name        | Description                                    |
|------|-------------|------------------------------------------------|
| 0    | Publish     | Publish telemetry to a NATS subject            |
| 1    | Subscribe   | Subscribe to commands or events                |
| 2    | Unsubscribe | Unsubscribe from a NATS subject                |
| 3    | Message     | Incoming command or event from subscription    |
| 4    | Request     | Request/reply (e.g., configuration request)    |
| 5    | Reply       | Reply to a request                             |
| 6    | Ack         | Subscription acknowledgment                    |
| 7    | Error       | Error message                                  |
| 8    | Auth        | Authentication message                         |
| 9    | Ping        | Keepalive ping                                 |
| 10   | Pong        | Keepalive pong response                        |

### Publishing Telemetry

```json
{
  "type": 0,
  "subject": "telemetry.line1.blister-sealer.temperature",
  "payload": {
    "value": 185.5,
    "unit": "C",
    "zone": "upper",
    "batchId": "B2024-001"
  },
  "correlationId": "msg-123"
}
```

### Subscribing to Commands

```json
{
  "type": 1,
  "subject": "commands.line1.blister-sealer.>"
}
```

### Receiving Commands

```json
{
  "type": 3,
  "subject": "commands.line1.blister-sealer.calibrate",
  "payload": {
    "action": "calibrate",
    "targetTemp": 185.0
  },
  "timestamp": "2024-01-15T10:30:00Z"
}
```

## Subject Hierarchy for Pharmaceutical Packaging

```
factory.{plant}.{line}.{equipment}.{metric}
│       │      │      │           └─ temperature, pressure, count, state, reject
│       │      │      └─ blister-sealer, cartoner, case-packer, vision-system
│       │      └─ line1, line2, serialization
│       └─ chicago, dublin, singapore
└─ Root for all factory telemetry

alerts.{plant}.{line}.{alert-type}
└─ temperature-excursion, specification-deviation, equipment-fault

batch.{batch-id}.{event}
└─ start, complete, hold, release

quality.{line}.{equipment}.{inspection-type}
└─ weight-check, vision-inspection, seal-integrity
```

## API Endpoints

| Endpoint   | Method | Description                          |
|------------|--------|--------------------------------------|
| `/ws`      | WS     | WebSocket endpoint for equipment     |
| `/health`  | GET    | Health check endpoint                |
| `/metrics` | GET    | Prometheus metrics endpoint          |
| `/devices` | GET    | List connected packaging equipment   |

## Documentation

| Document | Description |
|----------|-------------|
| [Video Series Overview](docs/video/SERIES_OVERVIEW.md) | 7-episode learning path |
| [WebSocket Developer Tutorial](docs/video/episodes/04-websocket-protocol/WS_DEVELOPER_TUTORIAL.md) | Hands-on protocol guide |
| [Monitoring Architecture](docs/monitoring/MONITORING_ARCHITECTURE.md) | PLG stack design |
| [Historical Data Retention](docs/compliance/HISTORICAL_DATA_RETENTION.md) | FDA 21 CFR Part 11 compliance |

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
│       ├── Auth/                      # Device authentication & authorization
│       ├── Configuration/             # Gateway and JetStream options
│       ├── Handlers/                  # WebSocket message handlers
│       ├── Models/                    # Message and device models
│       ├── Services/                  # NATS, metrics, connection services
│       └── Program.cs                 # Application entry point
├── tests/
│   └── NatsWebSocketBridge.Tests/     # Unit tests
└── docs/
    ├── video/                         # Video series content
    │   └── episodes/                  # Per-episode materials
    ├── monitoring/                    # PLG stack documentation
    └── compliance/                    # FDA compliance documentation
```

## License

MIT License - see LICENSE file for details.
