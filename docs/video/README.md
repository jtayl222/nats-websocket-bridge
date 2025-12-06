# Video Production Assets

Complete production package for the YouTube series: **"NATS WebSocket Bridge - Real-Time Device Communication"**

## Series Structure

A 7-episode deep dive into building production-grade IoT messaging infrastructure.

| # | Episode | Duration | Status |
|---|---------|----------|--------|
| 01 | [Introduction & Problem Space](./episodes/01-intro/) | 8-10 min | Draft |
| 02 | [NATS Fundamentals](./episodes/02-nats-fundamentals/) | 12-15 min | Draft |
| 03 | [Gateway Architecture](./episodes/03-gateway-architecture/) | 15-18 min | Draft |
| 04 | [WebSocket Protocol](./episodes/04-websocket-protocol/) | 10-12 min | Draft |
| 05 | [Device SDK (C++)](./episodes/05-device-sdk/) | 12-15 min | Draft |
| 06 | [Monitoring & Observability](./episodes/06-monitoring-observability/) | 12-15 min | Draft |
| 07 | [Historical Data Retention](./episodes/07-historical-retention/) | 15-18 min | Draft |

**Total Runtime:** ~85-100 minutes

See [SERIES_OVERVIEW.md](./SERIES_OVERVIEW.md) for detailed planning.

## Quick Start

```bash
# Episode content
ls episodes/

# Each episode contains:
# - README.md    (overview, outline, objectives)
# - slides.md    (slide content)
# - script.md    (narration)
# - demo.md      (terminal commands)
```

## Directory Structure

```
docs/video/
├── README.md                    # This file
├── SERIES_OVERVIEW.md           # Series planning
├── episodes/
│   ├── 01-intro/
│   ├── 02-nats-fundamentals/
│   ├── 03-gateway-architecture/
│   ├── 04-websocket-protocol/
│   ├── 05-device-sdk/
│   ├── 06-monitoring-observability/
│   └── 07-historical-retention/
└── legacy/                      # Original single-video assets
    ├── VIDEO_PRODUCTION_PLAN.md
    ├── SLIDE_DECK_OUTLINE.md
    ├── DEMO_SCRIPTS.md
    └── SPEAKER_NOTES.md
```

## Demo Environment

### Prerequisites
```bash
# NATS CLI
brew install nats-io/nats-tools/nats

# .NET 8.0 for gateway
dotnet --version  # Should be 8.0+

# Docker for infrastructure
docker --version
```

### Quick Start
```bash
# Start full infrastructure
docker-compose -f docker/monitoring/docker-compose.yml up -d
docker-compose -f docker/historian/docker-compose.yml up -d

# Run gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Monitor NATS
nats sub "factory.>"
```

## Production Checklist

### Per Episode
- [ ] Review README and outline
- [ ] Test all demo commands
- [ ] Create slides from slides.md
- [ ] Practice narration script
- [ ] Record slides + voiceover
- [ ] Record terminal demos
- [ ] Edit and assemble
- [ ] Add captions
- [ ] Create thumbnail
- [ ] Write description with timestamps

### Recording Settings
- Resolution: 1920x1080 (1080p)
- Font: JetBrains Mono for code
- Terminal theme: Dark with high contrast
- IDE zoom: 150%

## Related Resources

- [Presentation Diagrams](../diagrams/presentation-diagrams.md) - Mermaid diagrams
- [Sequence Diagrams](../diagrams/sequence-diagrams.md) - Protocol flows
- [Historical Data Retention](../compliance/HISTORICAL_DATA_RETENTION.md) - Episode 07 reference
