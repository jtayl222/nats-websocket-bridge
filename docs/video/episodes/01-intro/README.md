# Episode 01: Introduction & Problem Space

**Duration:** 8-10 minutes
**Prerequisites:** None

## Learning Objectives

By the end of this episode, viewers will understand:
- The challenges of IoT device communication at scale
- Why pharmaceutical manufacturing has unique requirements
- What NATS brings to the table vs traditional solutions
- Overview of the complete system architecture

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

## Resources to Mention

- NATS.io documentation
- GitHub repository link
- Synadia (NATS company)
