# Episode 02: NATS Fundamentals - Demo Script

## Pharmaceutical Packaging Line Scenario

This demo simulates telemetry from a blister packaging line:
- **Blister Sealer**: Temperature and pressure monitoring
- **Cartoner**: Product counts and reject detection
- **Quality Station**: Weight verification

## Setup

```bash
# Terminal layout: 3 panes
# Pane 1: NATS server logs
# Pane 2: Subscriber (simulates historian service)
# Pane 3: Publisher (simulates packaging equipment)

# Clear screen
clear
```

## Demo 1: Core Pub/Sub - Packaging Line Telemetry (3 min)

### Pane 1: Start NATS
```bash
docker run --rm -p 4222:4222 -p 8222:8222 --name nats nats:latest -js -m 8222
```

### Pane 2: Subscribe (Historian Service)
```bash
# Subscribe to all factory messages
nats sub "factory.>"
```

### Pane 3: Publish (Packaging Equipment)
```bash
# Blister sealer temperature reading
nats pub factory.chicago.line1.blister-sealer.temperature '{"value":185.5,"unit":"C","zone":"upper","batchId":"B2024-001"}'

# Blister sealer pressure reading
nats pub factory.chicago.line1.blister-sealer.pressure '{"value":2.4,"unit":"bar","batchId":"B2024-001"}'

# Cartoner product count
nats pub factory.chicago.line1.cartoner.count '{"produced":1250,"rejected":3,"batchId":"B2024-001"}'

# Quality station weight check
nats pub factory.chicago.line1.quality.weight '{"measured":5.234,"target":5.200,"tolerance":0.050,"status":"pass"}'
```

**Talking Point:** Notice how the subscriber receives all messages matching `factory.>`. The `>` wildcard matches any number of tokens. Each message includes `batchId` for FDA batch record traceability.

## Demo 2: Wildcards - Filtering by Equipment Type (2 min)

### Pane 2: New subscriber with single-token wildcard
```bash
# Only blister sealer metrics (any metric type)
nats sub "factory.chicago.line1.blister-sealer.*"
```

### Pane 3: Publish
```bash
# These match the blister-sealer.* pattern
nats pub factory.chicago.line1.blister-sealer.temperature '{"value":186.0,"batchId":"B2024-001"}'
nats pub factory.chicago.line1.blister-sealer.pressure '{"value":2.5,"batchId":"B2024-001"}'

# These do NOT match (different equipment)
nats pub factory.chicago.line1.cartoner.count '{"value":1300}'
nats pub factory.chicago.line2.blister-sealer.temperature '{"value":185.0}'  # Different line
```

**Talking Point:** Wildcards enable selective monitoring. Quality teams can subscribe to `alerts.>`, while equipment controllers subscribe only to their commands.

## Demo 3: JetStream Stream - FDA-Compliant Retention (3 min)

### Pane 3: Create stream
```bash
# Create the TELEMETRY stream (7-day hot tier for pharmaceutical data)
nats stream add TELEMETRY \
  --subjects "factory.>" \
  --storage file \
  --retention limits \
  --max-age 7d \
  --max-msgs 1000000 \
  --discard old \
  --description "Packaging line telemetry - FDA 21 CFR Part 11 hot tier"

# View stream info
nats stream info TELEMETRY
```

### Simulate Batch Production
```bash
# Simulate a batch run with temperature readings
for i in {1..5}; do
  temp=$((183 + i))
  nats pub factory.chicago.line1.blister-sealer.temperature \
    "{\"value\":${temp}.5,\"unit\":\"C\",\"batchId\":\"B2024-001\",\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"
  sleep 0.5
done
```

### View stored messages (Batch Investigation)
```bash
# Historian can replay all data for batch investigation
nats stream view TELEMETRY --last 5
```

**Talking Point:** Messages are now persisted. For FDA compliance, we need to investigate "what happened during batch B2024-001 three weeks ago?" JetStream provides the hot tier for immediate queries.

## Demo 4: Consumers - Historian Service Pattern (3 min)

### Create a pull consumer (Historian Service)
```bash
# The historian service uses a durable consumer to process telemetry
nats consumer add TELEMETRY telemetry-historian \
  --pull \
  --deliver all \
  --ack explicit \
  --max-deliver 5 \
  --filter "factory.>" \
  --description "TimescaleDB historian ingestion"

nats consumer info TELEMETRY telemetry-historian
```

### Consume messages (Simulating Historian)
```bash
# Fetch messages for batch processing into TimescaleDB
nats consumer next TELEMETRY telemetry-historian --count 3

# Show pending count - historian tracks its position
nats consumer info TELEMETRY telemetry-historian
```

### Batch Investigation Replay
```bash
# Quality team needs to investigate a batch deviation
# Create a replay consumer to re-analyze all data
nats consumer add TELEMETRY batch-investigation \
  --pull \
  --deliver all \
  --ack none \
  --description "Batch B2024-001 investigation replay"

# Replay all historical messages for analysis
nats consumer next TELEMETRY batch-investigation --count 10
```

**Talking Point:** This is critical for pharmaceutical compliance. When QA needs to investigate a batch deviation, they can replay all telemetry. The historian service uses durable consumers to ensure no data is lost during service restarts.

## Cleanup

```bash
docker stop nats
```

## Key Takeaways for Pharmaceutical Manufacturing

1. **Subject Hierarchy**: `factory.{plant}.{line}.{equipment}.{metric}` enables precise filtering
2. **Batch Association**: Every message includes `batchId` for FDA batch record traceability
3. **JetStream Persistence**: 7-day hot tier enables immediate batch investigation
4. **Consumer Replay**: Quality teams can re-analyze historical data for deviation investigation
