# Episode 02: Demo Script

## Setup

```bash
# Terminal layout: 3 panes
# Pane 1: NATS server logs
# Pane 2: Subscriber
# Pane 3: Publisher

# Clear screen
clear
```

## Demo 1: Core Pub/Sub (3 min)

### Pane 1: Start NATS
```bash
docker run --rm -p 4222:4222 -p 8222:8222 --name nats nats:latest -js -m 8222
```

### Pane 2: Subscribe
```bash
# Subscribe to all factory messages
nats sub "factory.>"
```

### Pane 3: Publish
```bash
# Publish a temperature reading
nats pub factory.line1.sensor.temp '{"device":"TEMP-001","value":23.5,"unit":"C"}'

# Publish to different subjects
nats pub factory.line1.sensor.pressure '{"device":"PRES-001","value":101.3,"unit":"kPa"}'
nats pub factory.line2.sensor.temp '{"device":"TEMP-002","value":24.1,"unit":"C"}'

# Show wildcard matching
nats pub factory.line1.plc.status '{"state":"running"}'
```

**Talking Point:** Notice how the subscriber receives all messages matching `factory.>`. The `>` wildcard matches any number of tokens.

## Demo 2: Wildcards (2 min)

### Pane 2: New subscriber with single-token wildcard
```bash
# Only line1 sensors
nats sub "factory.line1.sensor.*"
```

### Pane 3: Publish
```bash
nats pub factory.line1.sensor.temp '{"value":23.6}'    # Matches
nats pub factory.line1.sensor.humidity '{"value":45}'  # Matches
nats pub factory.line2.sensor.temp '{"value":24.0}'    # No match
nats pub factory.line1.plc.status '{"state":"idle"}'   # No match
```

## Demo 3: JetStream Stream (3 min)

### Pane 3: Create stream
```bash
# Create the TELEMETRY stream
nats stream add TELEMETRY \
  --subjects "factory.>" \
  --storage file \
  --retention limits \
  --max-age 7d \
  --max-msgs 1000000 \
  --discard old

# View stream info
nats stream info TELEMETRY
```

### Publish some messages
```bash
# Publish 5 messages
for i in {1..5}; do
  nats pub factory.line1.sensor.temp "{\"reading\":$i,\"value\":$((20+i))}"
  sleep 0.5
done
```

### View stored messages
```bash
nats stream view TELEMETRY --last 5
```

**Talking Point:** Messages are now persisted. Even if no subscribers are online, the data is safely stored.

## Demo 4: Consumers (3 min)

### Create a pull consumer
```bash
nats consumer add TELEMETRY historian \
  --pull \
  --deliver all \
  --ack explicit \
  --max-deliver 5 \
  --filter "factory.>"

nats consumer info TELEMETRY historian
```

### Consume messages
```bash
# Fetch messages
nats consumer next TELEMETRY historian --count 3

# Show pending count
nats consumer info TELEMETRY historian
```

### Show replay capability
```bash
# Create another consumer starting from beginning
nats consumer add TELEMETRY replay-demo \
  --pull \
  --deliver all \
  --ack none

# Get all historical messages
nats consumer next TELEMETRY replay-demo --count 10
```

**Talking Point:** This is the power of JetStream. New consumers can replay history. If a service restarts, it picks up where it left off.

## Cleanup

```bash
docker stop nats
```
