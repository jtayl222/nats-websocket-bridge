# Episode 01: Slides

## Slide 1: Title
**NATS WebSocket Bridge**
*Real-Time Device Communication for Manufacturing*

Episode 1 of 7

---

## Slide 2: The Question
> "What happens when 10,000 sensors need to talk to your cloud...
> in real-time...
> with guaranteed delivery...
> and you can't lose a single reading?"

---

## Slide 3: PharmaCo Scenario

**Smart Packaging Line**
- 47 devices per line
- 12 production lines
- ~500 devices total
- 10,000+ messages/second at peak

*Compliance: FDA 21 CFR Part 11*

---

## Slide 4: The Challenges

| Challenge | Impact |
|-----------|--------|
| Network drops | Lost telemetry = compliance gaps |
| High throughput | Traditional HTTP can't keep up |
| Message ordering | Out-of-order = incorrect batch records |
| Historical replay | "What happened 3 weeks ago?" |
| Multi-protocol | PLCs, sensors, vision systems |

---

## Slide 5: Traditional Approaches

**HTTP Polling**
- High latency (seconds)
- Wasted bandwidth
- No push capability

**MQTT**
- Good pub/sub model
- Clustering is complex
- No built-in persistence

**Kafka**
- Powerful but heavy
- Operational burden
- Not designed for edge

---

## Slide 6: Why NATS?

```
┌─────────────────────────────────────┐
│           NATS.io                   │
├─────────────────────────────────────┤
│ ✓ Single binary, ~20MB             │
│ ✓ Zero external dependencies       │
│ ✓ 10M+ messages/second             │
│ ✓ JetStream persistence            │
│ ✓ Built-in clustering              │
│ ✓ Subject-based routing            │
│ ✓ At-most-once or exactly-once     │
└─────────────────────────────────────┘
```

---

## Slide 7: Architecture Overview

```
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│  Device  │────▶│ Gateway  │────▶│   NATS   │────▶│Historian │
│  (C++)   │ WS  │  (C#)    │     │JetStream │     │TimescaleDB│
└──────────┘     └──────────┘     └──────────┘     └──────────┘
                      │
                      ▼
                ┌──────────┐
                │Prometheus│
                │ Grafana  │
                └──────────┘
```

---

## Slide 8: What We'll Build

| Episode | Topic |
|---------|-------|
| 02 | NATS Fundamentals |
| 03 | Gateway Architecture |
| 04 | WebSocket Protocol |
| 05 | Device SDK (C++) |
| 06 | Monitoring |
| 07 | Historical Retention |

---

## Slide 9: Let's Begin

**Next Episode:** NATS Fundamentals
- Core concepts
- Pub/Sub patterns
- JetStream deep dive

*Subscribe for updates*

GitHub: `github.com/[repo]`
