# Episode 06: Monitoring & Observability

**Duration:** 12-15 minutes
**Prerequisites:** Episodes 01-03

## Learning Objectives

By the end of this episode, viewers will understand:
- PLG stack: Prometheus, Loki, Grafana
- Gateway metrics instrumentation
- Structured logging for Loki
- Building dashboards and alerts

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
