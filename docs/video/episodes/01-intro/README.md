# Episode 01: Introduction & Problem Space

**Duration:** 8-10 minutes
**Prerequisites:** None
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (1 of 7)

## Learning Objectives

By the end of this episode, viewers will understand:
- The challenges of IoT device communication at scale in pharmaceutical manufacturing
- Why pharmaceutical packaging lines have unique requirements (FDA 21 CFR Part 11, ALCOA+)
- What NATS brings to the table vs traditional solutions (MQTT, Kafka, custom HTTP)
- Overview of the complete system architecture from device to historian

## Outline

1. **Hook** (0:00-0:30)
   - "What happens when 10,000 sensors need to talk to your cloud?"
   - Quick visual of factory floor with devices

2. **The Manufacturing Challenge** (0:30-2:30)
   - Real-time telemetry requirements
   - Compliance and audit trails
   - Network unreliability in factory environments
   - Scale: thousands of devices, millions of messages

3. **Traditional Approaches & Their Limits** (2:30-4:00)
   - HTTP polling: latency, overhead
   - MQTT: good but clustering complexity
   - Kafka: overkill for edge, operational burden
   - Custom solutions: maintenance nightmare

4. **Enter NATS** (4:00-6:00)
   - Single binary, zero dependencies
   - Designed for cloud-native and edge
   - JetStream for persistence and replay
   - Built-in clustering and failover

5. **System Architecture Overview** (6:00-8:00)
   - Mermaid diagram walkthrough
   - Components: Devices → WebSocket → Gateway → NATS → Historian
   - Data flow explanation

6. **What We'll Build** (8:00-9:00)
   - Preview of each episode
   - Call to action: subscribe, follow along

## Key Visuals

- Factory floor illustration with sensors
- Architecture diagram (animated reveal)
- Comparison table: NATS vs alternatives
- PharmaCo logo/branding for context

## Demo

None for this episode - conceptual introduction only.

## Pharmaceutical Manufacturing Context

This episode establishes the business context for pharmaceutical packaging lines:

| Challenge | Impact | How We Address It |
|-----------|--------|-------------------|
| Batch traceability | FDA requires complete batch records | Every message associated with batch_id |
| Network unreliability | Factory floor WiFi/Ethernet issues | Offline buffering in Device SDK |
| Data integrity | ALCOA+ compliance requirements | Checksums, audit trails, immutable logs |
| Real-time visibility | OEE monitoring, deviation detection | Sub-second message delivery via NATS |

**Packaging Line Examples Used Throughout Series:**
- Blister line temperature monitoring (seal quality)
- Cartoner photo-eye counts (products per minute)
- Case packer weight verification (reject detection)
- Serialization aggregation (track & trace compliance)

## Resources to Mention

- NATS.io documentation
- GitHub repository link
- Synadia (NATS company)

## Next Episode

→ [Episode 02: NATS Fundamentals](../02-nats-fundamentals/README.md) - Deep dive into NATS core concepts and JetStream
