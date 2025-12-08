# JetStream vs Core NATS: Choosing the Right Messaging Pattern

> **Video Series Reference:** This document supplements [Episode 02: NATS Fundamentals](../video/episodes/02-nats-fundamentals/README.md) and [Episode 07: Historical Data Retention](../video/episodes/07-historical-retention/README.md).

## Overview

The NATS WebSocket Bridge Gateway supports both NATS Core pub/sub and JetStream messaging through the `IJetStreamNatsService` interface. This document explains when to use each approach for pharmaceutical packaging line applications.

## Quick Comparison

| Feature | NATS Core | JetStream |
|---------|-----------|-----------|
| **Message Persistence** | None (fire-and-forget) | Configurable retention (time, count, bytes) |
| **Delivery Guarantee** | At-most-once | At-least-once, exactly-once available |
| **Message Replay** | Not possible | Full history replay capability |
| **Acknowledgements** | None | Explicit, automatic, or none |
| **Consumer State** | Ephemeral only | Durable consumers survive restarts |
| **Latency** | ~100μs | ~200-500μs (persistence overhead) |
| **Throughput** | Higher | Slightly lower due to persistence |
| **FDA Compliance** | Not suitable | Designed for regulated environments |

## IJetStreamNatsService Capabilities

The gateway's `IJetStreamNatsService` interface (`src/NatsWebSocketBridge.Gateway/Services/IJetStreamNatsService.cs`) provides:

### Publishing with Acknowledgement
```csharp
Task<JetStreamPublishResult> PublishAsync(
    string subject,
    byte[] data,
    Dictionary<string, string>? headers = null,
    string? messageId = null,  // For deduplication
    CancellationToken cancellationToken = default);
```

**JetStreamPublishResult** includes:
- `Success`: Confirmation message was persisted
- `Stream`: Which stream accepted the message
- `Sequence`: Unique sequence number for audit trail
- `Duplicate`: Deduplication detection
- `RetryCount`: For operational visibility

### Message Consumption with Replay
```csharp
Task<JetStreamSubscription> SubscribeWithReplayAsync(
    string streamName,
    string subject,
    string consumerNamePrefix,
    ReplayOptions replayOptions,  // Start from: All, New, LastReceived, ByTime
    Func<JetStreamMessage, Task> handler,
    string? deviceId = null,
    CancellationToken cancellationToken = default);
```

### Message Acknowledgement Options
```csharp
Task AckMessageAsync(JetStreamMessage message, ...);       // Success
Task NakMessageAsync(JetStreamMessage message, ...);       // Retry later
Task InProgressAsync(JetStreamMessage message, ...);       // Extend deadline
Task TerminateMessageAsync(JetStreamMessage message, ...); // Never retry
```

## When to Use JetStream (Recommended for Pharma)

### ✅ Use JetStream For:

| Use Case | Rationale | Pharma Example |
|----------|-----------|----------------|
| **Telemetry Data** | Persistence for batch investigation | Blister sealer temperature readings |
| **Quality Events** | Audit trail requirement | Vision system reject events |
| **Batch Records** | FDA 21 CFR Part 11 compliance | Batch start/stop events |
| **Alert History** | Root cause analysis | Temperature excursion alerts |
| **Commands Requiring Confirmation** | Delivery guarantee | Recipe downloads to equipment |

### JetStream Configuration Example (appsettings.json)

```json
{
  "JetStream": {
    "Enabled": true,
    "Streams": [
      {
        "Name": "TELEMETRY",
        "Subjects": ["factory.>", "telemetry.>"],
        "Description": "Packaging line telemetry - 7 day hot tier",
        "Retention": "Limits",
        "Storage": "File",
        "MaxAge": "7d",
        "MaxMessages": 100000,
        "Replicas": 1,
        "AllowDirect": true
      },
      {
        "Name": "ALERTS",
        "Subjects": ["alerts.>"],
        "Description": "Critical alerts - 30 day retention",
        "Retention": "Limits",
        "Storage": "File",
        "MaxAge": "30d",
        "DenyDelete": true,
        "DenyPurge": true
      },
      {
        "Name": "QUALITY",
        "Subjects": ["quality.>"],
        "Description": "Quality inspection results",
        "Retention": "Limits",
        "Storage": "File",
        "MaxAge": "30d",
        "DenyDelete": true,
        "DenyPurge": true
      }
    ],
    "Consumers": [
      {
        "Name": "telemetry-historian",
        "StreamName": "TELEMETRY",
        "FilterSubject": "factory.>",
        "DeliveryPolicy": "All",
        "AckPolicy": "Explicit",
        "AckWait": "30s",
        "MaxDeliver": 5,
        "Description": "TimescaleDB historian ingestion"
      }
    ]
  }
}
```

## When to Use Core NATS

### ✅ Use Core NATS For:

| Use Case | Rationale | Pharma Example |
|----------|-----------|----------------|
| **Real-time Control** | Lowest latency required | E-stop propagation |
| **Ephemeral Status** | No persistence needed | "I'm alive" heartbeats |
| **High-frequency Metrics** | Volume too high to persist all | 1000Hz vibration data (sample and persist) |
| **Request/Reply** | Synchronous pattern | Configuration queries |

### Core NATS Scenarios

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    CORE NATS USE CASES (Ephemeral)                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  E-STOP Broadcast (Latency Critical)                                        │
│  ┌──────────┐         ┌──────────┐         ┌──────────┐                    │
│  │ E-Stop   │ ───────►│   NATS   │────────►│ All Line │                    │
│  │ Button   │  <1ms   │  (Core)  │  <1ms   │ Equipment│                    │
│  └──────────┘         └──────────┘         └──────────┘                    │
│                                                                             │
│  Heartbeat/Status (High Frequency, No Persistence)                         │
│  ┌──────────┐         ┌──────────┐         ┌──────────┐                    │
│  │Equipment │ ───────►│   NATS   │────────►│Dashboard │                    │
│  │  10/sec  │         │  (Core)  │         │ (latest) │                    │
│  └──────────┘         └──────────┘         └──────────┘                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Pros and Cons Analysis

### JetStream Advantages

| Advantage | Impact for Pharmaceutical Manufacturing |
|-----------|----------------------------------------|
| **Message Persistence** | Enables "what happened 3 weeks ago?" investigations |
| **Delivery Guarantees** | No lost batch records or quality events |
| **Replay Capability** | New services can catch up on historical data |
| **Sequence Numbers** | Audit trail with ordered, numbered messages |
| **Deduplication** | Prevents duplicate records from network retries |
| **Consumer State** | Historian service survives restarts without data loss |
| **Acknowledgements** | Confirm processing before removing from queue |
| **Retention Policies** | Automatic data lifecycle management |

### JetStream Disadvantages

| Disadvantage | Mitigation |
|--------------|------------|
| **Higher Latency** (~2-5x core) | Acceptable for telemetry; use Core for E-stops |
| **Storage Requirements** | Configure appropriate retention; use compression |
| **Operational Complexity** | Stream/consumer management required |
| **Slightly Lower Throughput** | Still handles 100K+ msg/sec per stream |
| **Learning Curve** | More concepts (streams, consumers, ack policies) |

### Core NATS Advantages

| Advantage | Impact |
|-----------|--------|
| **Lowest Latency** | Critical for safety-related messaging |
| **Simplest Model** | Easy to understand and debug |
| **Highest Throughput** | Millions of messages per second |
| **No Storage Overhead** | Minimal resource usage |
| **Simpler Operations** | No streams/consumers to manage |

### Core NATS Disadvantages

| Disadvantage | Impact for Pharmaceutical Manufacturing |
|--------------|----------------------------------------|
| **No Persistence** | Cannot investigate historical events |
| **No Replay** | New subscribers miss past messages |
| **At-most-once Delivery** | Messages can be lost |
| **No Acknowledgements** | No confirmation of processing |
| **Not Audit-Friendly** | No sequence numbers or timestamps from server |

## Recommended Architecture for Pharma Packaging

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    HYBRID MESSAGING ARCHITECTURE                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PACKAGING LINE EQUIPMENT                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                                                                      │   │
│  │  Blister Sealer    Cartoner       Case Packer    Serialization      │   │
│  │       │                │               │              │              │   │
│  └───────┼────────────────┼───────────────┼──────────────┼──────────────┘   │
│          │                │               │              │                   │
│          ▼                ▼               ▼              ▼                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         GATEWAY                                      │   │
│  │                                                                      │   │
│  │   ┌─────────────────────────────────────────────────────────────┐   │   │
│  │   │              IJetStreamNatsService                           │   │   │
│  │   │                                                              │   │   │
│  │   │  ┌─────────────────────┐  ┌─────────────────────┐           │   │   │
│  │   │  │   JetStream Path    │  │   Core NATS Path    │           │   │   │
│  │   │  │   (Persistent)      │  │   (Ephemeral)       │           │   │   │
│  │   │  │                     │  │                     │           │   │   │
│  │   │  │ • Telemetry         │  │ • E-Stop broadcast  │           │   │   │
│  │   │  │ • Quality events    │  │ • Heartbeats        │           │   │   │
│  │   │  │ • Batch records     │  │ • Request/Reply     │           │   │   │
│  │   │  │ • Alerts            │  │ • Status queries    │           │   │   │
│  │   │  │ • Commands          │  │                     │           │   │   │
│  │   │  └──────────┬──────────┘  └──────────┬──────────┘           │   │   │
│  │   │             │                        │                       │   │   │
│  │   └─────────────┼────────────────────────┼───────────────────────┘   │   │
│  │                 │                        │                           │   │
│  └─────────────────┼────────────────────────┼───────────────────────────┘   │
│                    │                        │                               │
│                    ▼                        ▼                               │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    NATS SERVER + JETSTREAM                          │   │
│  │                                                                      │   │
│  │  ┌─────────────────────────┐  ┌─────────────────────────────────┐   │   │
│  │  │      STREAMS            │  │        CORE PUB/SUB             │   │   │
│  │  │                         │  │                                  │   │   │
│  │  │  TELEMETRY (7 days)     │  │  estop.>  (no persistence)      │   │   │
│  │  │  EVENTS (14 days)       │  │  status.> (no persistence)      │   │   │
│  │  │  ALERTS (30 days)       │  │                                  │   │   │
│  │  │  QUALITY (30 days)      │  │                                  │   │   │
│  │  │  BATCHES (90 days)      │  │                                  │   │   │
│  │  └─────────────────────────┘  └─────────────────────────────────┘   │   │
│  │                                                                      │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Decision Matrix

Use this matrix to decide which messaging pattern to use:

| Question | If Yes → | If No → |
|----------|----------|---------|
| Does FDA require this data in batch records? | JetStream | Either |
| Do I need to investigate "what happened last week"? | JetStream | Either |
| Can the system lose this message? | Core NATS ok | JetStream |
| Is sub-millisecond latency critical? | Core NATS | JetStream ok |
| Do new subscribers need historical data? | JetStream | Either |
| Is this high-frequency data (>100/sec)? | Consider sampling + JetStream | JetStream |
| Is this a safety-critical broadcast? | Core NATS | JetStream ok |

## Implementation Patterns

### Pattern 1: All JetStream (Simplest for Compliance)
```csharp
// Configure all subjects to flow through JetStream
// Simplest for FDA compliance, slightly higher latency
await jetStreamService.PublishAsync(
    "telemetry.line1.blister-sealer.temperature",
    payload,
    headers: new Dictionary<string, string>
    {
        ["batchId"] = "B2024-001",
        ["deviceId"] = "blister-sealer-01"
    });
```

### Pattern 2: Hybrid (Recommended)
```csharp
// JetStream for persistent data
if (RequiresPersistence(subject))
{
    await jetStreamService.PublishAsync(subject, payload);
}
else
{
    // Core NATS for ephemeral/latency-critical
    await coreNatsService.PublishAsync(subject, payload);
}
```

### Pattern 3: Sampling for High-Frequency Data
```csharp
// 1000Hz vibration data - sample every 100th for persistence
if (messageCount % 100 == 0)
{
    await jetStreamService.PublishAsync(
        "telemetry.line1.motor.vibration.sampled",
        payload);
}
// All data to real-time dashboard via Core NATS
await coreNatsService.PublishAsync(
    "realtime.line1.motor.vibration",
    payload);
```

## Performance Considerations

### Latency Comparison
| Operation | Core NATS | JetStream |
|-----------|-----------|-----------|
| Publish (no ack) | ~100μs | N/A |
| Publish (with ack) | N/A | ~200-500μs |
| Subscribe delivery | ~50μs | ~100μs |
| Request/Reply | ~200μs | ~400-600μs |

### Throughput Comparison (Single Stream)
| Scenario | Core NATS | JetStream (File) | JetStream (Memory) |
|----------|-----------|------------------|-------------------|
| Small messages (100B) | 10M+ msg/sec | 100-500K msg/sec | 500K-1M msg/sec |
| Medium messages (1KB) | 1M+ msg/sec | 50-200K msg/sec | 200-500K msg/sec |
| Large messages (10KB) | 100K+ msg/sec | 10-50K msg/sec | 50-100K msg/sec |

*Note: Actual performance depends on hardware, network, and configuration.*

## Related Documentation

- [Historical Data Retention](../compliance/HISTORICAL_DATA_RETENTION.md) - FDA 21 CFR Part 11 compliance
- [Episode 02: NATS Fundamentals](../video/episodes/02-nats-fundamentals/README.md) - JetStream basics
- [Episode 07: Historical Retention](../video/episodes/07-historical-retention/README.md) - Compliance architecture
- [appsettings.json](../../src/NatsWebSocketBridge.Gateway/appsettings.json) - Stream configuration
