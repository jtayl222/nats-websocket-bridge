# Episode 02: NATS Fundamentals

**Duration:** 12-15 minutes
**Prerequisites:** [Episode 01: Introduction](../01-intro/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (2 of 7)

## Learning Objectives

By the end of this episode, viewers will understand:
- NATS core concepts: subjects, pub/sub, request/reply
- JetStream streams and consumers for durable message storage
- Message persistence and replay for batch investigation
- Practical subject hierarchy design for pharmaceutical packaging lines

## Outline

1. **Recap & Setup** (0:00-1:00)
   - Quick architecture reminder
   - Starting NATS with Docker

2. **Core NATS Concepts** (1:00-4:00)
   - Subjects as addresses
   - Wildcards: `*` and `>`
   - Pub/sub pattern
   - Request/reply pattern
   - Demo: CLI pub/sub

3. **The Problem with Core NATS** (4:00-5:00)
   - Fire and forget
   - No persistence
   - What if subscriber is offline?

4. **JetStream Introduction** (5:00-8:00)
   - Streams: durable message storage
   - Consumers: how clients read
   - Acknowledgements and redelivery
   - Demo: Creating a stream

5. **Consumer Types** (8:00-10:00)
   - Push vs Pull consumers
   - Durable vs Ephemeral
   - Delivery policies: All, New, Last
   - Demo: Creating consumers

6. **Subject Design for Manufacturing** (10:00-13:00)
   - Hierarchy: `factory.line1.sensor.temperature`
   - Stream subjects: `factory.>`
   - Filtering with wildcards
   - Best practices

7. **Wrap-up** (13:00-14:00)
   - Key takeaways
   - Preview: Gateway architecture

## Demo Commands

```bash
# Start NATS with JetStream
docker run -p 4222:4222 -p 8222:8222 nats:latest -js

# Subscribe to all factory messages
nats sub "factory.>"

# Publish telemetry
nats pub factory.line1.sensor.temp '{"value": 23.5}'

# Create a stream
nats stream add TELEMETRY --subjects "factory.>" --storage file --retention limits --max-age 7d

# Create a consumer
nats consumer add TELEMETRY historian --pull --deliver all --ack explicit
```

## Key Visuals

- Subject hierarchy tree diagram
- Stream vs Consumer relationship
- Message flow animation
- Acknowledgement sequence diagram

## Subject Design for Pharmaceutical Packaging

The subject hierarchy maps directly to packaging line organization:

```
factory.{plant}.{line}.{device}.{metric}
│       │      │      │        └─ temperature, pressure, count, state
│       │      │      └─ blister-sealer-01, cartoner-02, case-packer-01
│       │      └─ line1, line2, serialization
│       └─ chicago, dublin, singapore
└─ Root for all factory telemetry
```

**Example subjects for a blister packaging line:**
- `factory.chicago.line1.blister-sealer-01.temperature` - Seal temperature
- `factory.chicago.line1.vision-system-01.reject` - Reject events
- `alerts.chicago.line1.temperature-excursion` - Critical alerts
- `batch.B2024-001.start` - Batch lifecycle events

## Related Documentation

- [JetStream vs Core NATS](../../../architecture/JETSTREAM_VS_CORE_NATS.md) - Detailed pros/cons comparison for pharma applications
- [Historical Data Retention](../../../compliance/HISTORICAL_DATA_RETENTION.md) - How JetStream feeds the historian service
- [Episode 07: Historical Retention](../07-historical-retention/README.md) - Deep dive into data archival

## Next Episode

→ [Episode 03: Gateway Architecture](../03-gateway-architecture/README.md) - Building the C# WebSocket gateway
