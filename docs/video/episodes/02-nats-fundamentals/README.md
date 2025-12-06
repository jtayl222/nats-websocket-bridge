# Episode 02: NATS Fundamentals

**Duration:** 12-15 minutes
**Prerequisites:** Episode 01

## Learning Objectives

By the end of this episode, viewers will understand:
- NATS core concepts: subjects, pub/sub, request/reply
- JetStream streams and consumers
- Message persistence and replay
- Practical subject hierarchy design

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
