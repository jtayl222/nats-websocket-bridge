# System Diagrams for Pharmaceutical Manufacturing Gateway

This directory contains UML diagrams for the NATS WebSocket Bridge Gateway system, designed for pharmaceutical packaging line connectivity.

## Diagram Index

1. [Sequence Diagrams](./sequence-diagrams.md)
   - Packaging Equipment Authentication
   - Telemetry Publish Flow (temperature, pressure, counts)
   - Command Subscribe Flow (calibration, recipes)
   - Alert Broadcast (temperature excursions, equipment faults)
   - Network Reconnection with Buffered Message Replay
   - Request/Reply (configuration queries)

2. [Component Diagrams](./component-diagrams.md)
   - Pharmaceutical Manufacturing System Architecture
   - Gateway Components (Auth, Validation, Routing)
   - Device SDK Components (offline buffering, metrics)

3. [State Diagrams](./state-diagrams.md)
   - WebSocket Connection State Machine
   - Packaging Equipment Lifecycle
   - Blister Sealer State Machine (example)

4. [Class Diagrams](./class-diagrams.md)
   - Gateway Services
   - C++ SDK Classes
   - Message Models (telemetry, alerts, batch events)

## Pharmaceutical Context

These diagrams illustrate patterns for:

| Equipment Type | Example Flows |
|---------------|---------------|
| Blister Sealers | Temperature monitoring, seal pressure validation |
| Cartoners | Product count telemetry, reject events |
| Case Packers | Weight verification, barcode confirmation |
| Serialization | Serial number validation, aggregation events |

## Related Documentation

- [Video Series Overview](../video/SERIES_OVERVIEW.md) - 7-episode learning path
- [Historical Data Retention](../compliance/HISTORICAL_DATA_RETENTION.md) - FDA 21 CFR Part 11 compliance
- [Monitoring Architecture](../monitoring/MONITORING_ARCHITECTURE.md) - PLG stack design
