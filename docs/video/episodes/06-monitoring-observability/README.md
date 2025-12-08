# Episode 06: Monitoring & Observability

**Duration:** 12-15 minutes
**Prerequisites:** [Episode 01](../01-intro/README.md) through [Episode 03](../03-gateway-architecture/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (6 of 7)

> **Technical Reference:** For detailed architecture, metrics definitions, and alerting rules, see [Monitoring Architecture](../../../monitoring/MONITORING_ARCHITECTURE.md).

## Learning Objectives

By the end of this episode, viewers will understand:
- PLG stack: Prometheus, Loki, Grafana (and why not ELK)
- Gateway metrics instrumentation for production visibility
- Structured logging for Loki with pharmaceutical audit context
- Building dashboards and alerts for packaging line operations

## Outline

1. **Why Observability Matters** (0:00-1:30)
   - "You can't fix what you can't see"
   - Manufacturing uptime requirements
   - Debugging distributed systems
   - Compliance audit trails

2. **The PLG Stack** (1:30-3:30)
   - Prometheus: Metrics
   - Loki: Logs
   - Grafana: Visualization
   - Why not ELK?

3. **Prometheus Metrics** (3:30-6:30)
   - Counter, Gauge, Histogram
   - GatewayMetrics service walkthrough
   - OpenTelemetry integration
   - Demo: /metrics endpoint

4. **Structured Logging** (6:30-9:00)
   - Serilog configuration
   - JSON log format
   - Log levels and filtering
   - Loki ingestion
   - Demo: Log queries

5. **Grafana Dashboards** (9:00-12:00)
   - Gateway overview dashboard
   - Key panels: connections, throughput, latency
   - Creating custom panels
   - Demo: Dashboard tour

6. **Alerting** (12:00-14:00)
   - Prometheus alerting rules
   - Alert conditions
   - Notification channels
   - Demo: Trigger an alert

## Docker Compose Stack

```yaml
# docker/monitoring/docker-compose.yml
services:
  prometheus:
    image: prom/prometheus
    ports: ["9090:9090"]

  loki:
    image: grafana/loki
    ports: ["3100:3100"]

  grafana:
    image: grafana/grafana
    ports: ["3000:3000"]

  alertmanager:
    image: prom/alertmanager
    ports: ["9093:9093"]
```

## Key Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `gateway_connections_active` | Gauge | Current WebSocket connections |
| `gateway_messages_received_total` | Counter | Messages from devices |
| `gateway_message_processing_duration` | Histogram | Processing latency |
| `gateway_auth_attempts_total` | Counter | Auth success/failure |
| `gateway_nats_publish_duration` | Histogram | NATS latency |

## Demo Commands

```bash
# Start monitoring stack
docker-compose -f docker/monitoring/docker-compose.yml up -d

# View Prometheus targets
open http://localhost:9090/targets

# View Grafana
open http://localhost:3000  # admin/admin

# Query Loki logs
# {service="gateway"} |= "error"
```

## Key Visuals

- PLG stack architecture diagram
- Dashboard screenshot walkthrough
- Alert flow diagram
- Log query examples

## Pharmaceutical Operations Dashboards

Beyond system health, monitoring enables real-time manufacturing visibility:

### Factory Operations Dashboard
| Panel | Metric Source | Business Value |
|-------|---------------|----------------|
| Line Status | `factory_line_state` | Real-time production status |
| OEE Gauges | `factory_oee_*` | Availability, Performance, Quality |
| Reject Rate | `factory_defect_count_total` | Quality trend monitoring |
| E-Stop Events | `factory_emergency_stop_total` | Safety incident tracking |

### Compliance-Relevant Alerts
```yaml
# Alert on potential data integrity issues
- alert: DeviceClockDrift
  expr: abs(device_timestamp - server_timestamp) > 60
  labels:
    severity: warning
    compliance: ALCOA

# Alert on batch record gaps
- alert: TelemetryGap
  expr: time() - device_last_message_timestamp > 300
  labels:
    severity: critical
    compliance: "21_CFR_Part_11"
```

### Log Queries for Batch Investigation

When investigating a manufacturing deviation, Loki enables rapid log correlation:

```logql
# All events for a specific batch
{service="gateway"} | json | batch_id="B2024-001"

# Temperature excursions during production
{service="gateway"} | json | event_type="temperature_excursion" | batch_id="B2024-001"

# Device reconnections (potential data gaps)
{service="gateway"} |= "reconnected" | json | line_id="line1"
```

## Related Documentation

- [Monitoring Architecture](../../../monitoring/MONITORING_ARCHITECTURE.md) - Complete metrics and alerting reference
- [Episode 03: Gateway Architecture](../03-gateway-architecture/README.md) - Metrics instrumentation source
- [Episode 07: Historical Retention](../07-historical-retention/README.md) - Long-term data for compliance

## Next Episode

→ [Episode 07: Historical Data Retention](../07-historical-retention/README.md) - FDA compliance and long-term archival
