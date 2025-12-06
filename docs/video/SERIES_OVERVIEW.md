# NATS WebSocket Bridge - Video Series

A 7-episode deep dive into building production-grade IoT messaging infrastructure for pharmaceutical manufacturing.

## Series Structure

| Episode | Title | Duration | Focus |
|---------|-------|----------|-------|
| 01 | Introduction & Problem Space | 8-10 min | Why NATS, manufacturing challenges |
| 02 | NATS Fundamentals | 12-15 min | Core NATS, JetStream, subjects |
| 03 | Gateway Architecture | 15-18 min | C# WebSocket gateway design |
| 04 | WebSocket Protocol | 10-12 min | Authentication, message flow |
| 05 | Device SDK | 12-15 min | C++ SDK, embedded integration |
| 06 | Monitoring & Observability | 12-15 min | Prometheus, Grafana, Loki |
| 07 | Historical Data Retention | 15-18 min | TimescaleDB, compliance, archival |

**Total Runtime:** ~85-100 minutes

## Target Audience

- Backend/IoT engineers building device communication systems
- DevOps engineers designing observable infrastructure
- Architects evaluating messaging patterns for manufacturing
- Developers new to NATS wanting a real-world example

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

## Publishing Checklist

For each episode:
- [ ] Slides reviewed and exported
- [ ] Demo script tested end-to-end
- [ ] Narration script reviewed for clarity
- [ ] Recording completed
- [ ] Captions generated
- [ ] Thumbnail created
- [ ] Description and timestamps written
