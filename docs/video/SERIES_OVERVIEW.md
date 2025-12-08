# NATS WebSocket Bridge - Video Series

A 7-episode deep dive into building production-grade IoT messaging infrastructure for pharmaceutical manufacturing, demonstrating real-world solutions for FDA 21 CFR Part 11 compliance and ALCOA+ data integrity.

## Series Structure

| Episode | Title | Duration | Focus | Key Deliverables |
|---------|-------|----------|-------|------------------|
| [01](episodes/01-intro/README.md) | Introduction & Problem Space | 8-10 min | Why NATS, manufacturing challenges | Architecture overview, technology selection |
| [02](episodes/02-nats-fundamentals/README.md) | NATS Fundamentals | 12-15 min | Core NATS, JetStream, subjects | Hands-on NATS CLI demos |
| [03](episodes/03-gateway-architecture/README.md) | Gateway Architecture | 15-18 min | C# WebSocket gateway design | Production-ready gateway patterns |
| [04](episodes/04-websocket-protocol/README.md) | WebSocket Protocol | 10-12 min | Authentication, message flow | Protocol specification, [Developer Tutorial](episodes/04-websocket-protocol/WS_DEVELOPER_TUTORIAL.md) |
| [05](episodes/05-device-sdk/README.md) | Device SDK | 12-15 min | C++ SDK, embedded integration | Offline buffering, reconnection |
| [06](episodes/06-monitoring-observability/README.md) | Monitoring & Observability | 12-15 min | Prometheus, Grafana, Loki | Dashboards, alerting rules |
| [07](episodes/07-historical-retention/README.md) | Historical Data Retention | 15-18 min | TimescaleDB, compliance, archival | FDA compliance, audit trails |

**Total Runtime:** ~85-100 minutes

## Target Audience

### Primary: Technical Decision Makers & Implementers
- **Software Architects & CIOs** evaluating messaging patterns for regulated manufacturing
- **Backend/IoT Engineers** building device communication systems
- **DevOps Engineers** designing observable, compliant infrastructure
- **Developers** new to NATS wanting a real-world pharmaceutical manufacturing example

### Secondary: Pharmaceutical Industry Experts
- **Packaging Line Engineers** seeking to understand modern IoT connectivity for blister lines, cartoners, and case packers
- **Validation Engineers** evaluating systems for FDA 21 CFR Part 11 compliance
- **Quality Assurance Teams** needing to understand audit trail and data integrity capabilities
- **Operations Managers** interested in real-time OEE monitoring and predictive maintenance

### Pharmaceutical Manufacturing Context

This series uses pharmaceutical packaging line scenarios throughout, including:

| Equipment Type | Example Sensors | Typical Telemetry |
|---------------|-----------------|-------------------|
| Blister Lines | Temperature probes, seal pressure | Cavity fill detection, foil temperature |
| Cartoners | Photo-eyes, reject gates | Products per minute, reject counts |
| Case Packers | Weight scales, barcode scanners | Case counts, verification status |
| Serialization | Vision systems | Serial number validation, aggregation |

All examples demonstrate GxP-compliant patterns for batch record association, deviation tracking, and regulatory audit readiness.

## Episode Dependencies

```
01-intro ─────────────────────────────────────────────────────────▶
    │
    ├──▶ 02-nats ──▶ 03-gateway ──▶ 04-websocket ──▶ 05-sdk
    │                    │
    │                    └──────────▶ 06-monitoring
    │                    │
    │                    └──────────▶ 07-retention
```

### Learning Paths by Role

| Role | Recommended Path | Focus Areas |
|------|------------------|-------------|
| **Software Architect** | 01 → 02 → 03 → 06 → 07 | System design, compliance architecture |
| **Backend Developer** | 01 → 02 → 03 → 04 | Gateway implementation, protocol design |
| **Embedded Developer** | 01 → 02 → 04 → 05 | Device SDK, offline operation |
| **DevOps Engineer** | 01 → 02 → 03 → 06 | Monitoring, observability, deployment |
| **Validation Engineer** | 01 → 07 (detailed) | FDA compliance, audit trails, ALCOA+ |
| **Packaging Line Engineer** | 01 → 05 → 06 | Device connectivity, real-time monitoring |

- Episodes 01-05 should be watched in order (core learning path)
- Episodes 06-07 can be watched independently after 03

## Recording Guidelines

### Visual Consistency
- Resolution: 1920x1080 (1080p)
- Font: JetBrains Mono for code, Inter for slides
- Terminal theme: Dark with high contrast
- IDE: VS Code with dark theme, zoom level 150%

### Audio
- Clear narration, consistent volume
- No background music during demos
- Subtle transitions between sections

### Demo Environment
- Docker Compose for all infrastructure
- Scripted terminal commands (use demo scripts)
- Clean, purpose-built code examples

## File Structure Per Episode

```
episodes/
├── 01-intro/
│   ├── README.md          # Episode overview
│   ├── slides.md          # Slide content
│   ├── script.md          # Full narration script
│   └── demo.md            # Terminal demo steps
├── 02-nats-fundamentals/
│   └── ...
```

## Related Documentation

| Document | Description | Relevant Episodes |
|----------|-------------|-------------------|
| [Monitoring Architecture](../monitoring/MONITORING_ARCHITECTURE.md) | PLG stack design, metrics, alerting | Episode 06 |
| [Historical Data Retention](../compliance/HISTORICAL_DATA_RETENTION.md) | FDA 21 CFR Part 11, TimescaleDB, ALCOA+ | Episode 07 |
| [WebSocket Developer Tutorial](episodes/04-websocket-protocol/WS_DEVELOPER_TUTORIAL.md) | Hands-on protocol implementation guide | Episode 04 |
| [Architecture Diagrams](../diagrams/README.md) | System component and sequence diagrams | All episodes |

## Potential Future Episodes

Based on audience feedback and evolving needs, the following topics could warrant additional episodes:

| Topic | Target Audience | Rationale |
|-------|-----------------|-----------|
| **Deployment & Operations** | DevOps, IT | Kubernetes deployment, high availability, disaster recovery |
| **Security Deep Dive** | Security Engineers, Architects | TLS configuration, certificate management, network segmentation |
| **Serialization & Track-and-Trace** | Packaging Engineers, QA | DSCSA compliance, aggregation workflows, L4/L5 integration |
| **OPC UA Integration** | Automation Engineers | Bridging legacy PLCs to NATS via OPC UA |

**Current Assessment:** The 7-episode series provides comprehensive coverage for the primary and secondary audiences. Episodes 06 (Monitoring) and 07 (Historical Retention) address FDA compliance thoroughly. Additional episodes should be considered based on implementation feedback.

## Publishing Checklist

For each episode:
- [ ] Slides reviewed and exported
- [ ] Demo script tested end-to-end
- [ ] Narration script reviewed for clarity
- [ ] Recording completed
- [ ] Captions generated
- [ ] Thumbnail created
- [ ] Description and timestamps written
