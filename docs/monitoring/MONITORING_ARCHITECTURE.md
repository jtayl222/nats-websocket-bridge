# Monitoring Architecture

## Overview

This document describes the monitoring stack for the NATS WebSocket Bridge system, following industry best practices for industrial IoT and factory environments.

## Stack Selection: PLG (Prometheus + Loki + Grafana)

### Why PLG over ELK?

| Aspect | PLG Stack | ELK Stack |
|--------|-----------|-----------|
| **Operational Complexity** | Low - Loki is simple to deploy | High - Elasticsearch cluster management |
| **Resource Usage** | ~500MB RAM typical | 2-4GB+ RAM for Elasticsearch |
| **Query Language** | PromQL + LogQL (similar) | Lucene/KQL (different from metrics) |
| **Edge Deployment** | Excellent - lightweight | Challenging - heavy footprint |
| **NATS Integration** | Native Prometheus exporter | Requires additional tooling |
| **Cost** | Open source | Open source (or Elastic license) |
| **Unified UI** | Single Grafana for all | Kibana for logs, separate for metrics |

### Stack Components

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         MONITORING STACK                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐                 │
│  │ PROMETHEUS  │    │    LOKI     │    │   GRAFANA   │                 │
│  │             │    │             │    │             │                 │
│  │ • Metrics   │    │ • Logs      │    │ • Dashboards│                 │
│  │ • Alerts    │    │ • Labels    │    │ • Alerts    │                 │
│  │ • TSDB      │    │ • Retention │    │ • Explore   │                 │
│  └──────┬──────┘    └──────┬──────┘    └──────┬──────┘                 │
│         │                  │                  │                         │
│         └──────────────────┼──────────────────┘                         │
│                            │                                            │
│                     ┌──────▼──────┐                                     │
│                     │  ALERTING   │                                     │
│                     │             │                                     │
│                     │ • PagerDuty │                                     │
│                     │ • Slack     │                                     │
│                     │ • Email     │                                     │
│                     └─────────────┘                                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## Data Sources

### 1. NATS Server Metrics

NATS exposes Prometheus metrics natively at `/metrics`:

```yaml
# Key NATS metrics
nats_core_connection_count          # Active connections
nats_core_msg_sent_count            # Messages sent
nats_core_msg_recv_count            # Messages received
nats_core_bytes_sent                # Bytes sent
nats_core_bytes_recv                # Bytes received
nats_jetstream_stream_messages      # Messages in stream
nats_jetstream_consumer_pending     # Pending messages per consumer
nats_jetstream_consumer_ack_pending # Unacknowledged messages
```

### 2. Gateway Metrics (Custom)

```yaml
# Connection metrics
gateway_websocket_connections_total       # Total connections (counter)
gateway_websocket_connections_active      # Current active connections (gauge)
gateway_websocket_connection_duration_seconds  # Connection duration histogram

# Authentication metrics
gateway_auth_attempts_total{status="success|failure"}
gateway_auth_duration_seconds

# Message metrics
gateway_messages_received_total{type="publish|subscribe|..."}
gateway_messages_sent_total{type="message|ack|error|..."}
gateway_message_size_bytes                # Message size histogram
gateway_message_processing_duration_seconds

# Rate limiting metrics
gateway_rate_limit_rejections_total{device_id}
gateway_rate_limit_tokens_remaining{device_id}

# NATS bridge metrics
gateway_nats_publish_total{stream}
gateway_nats_publish_errors_total
gateway_nats_subscribe_total
gateway_nats_latency_seconds
```

### 3. Device Metrics (via SDK callbacks)

```yaml
# SDK-reported metrics (per device)
device_connection_state{device_id, device_type}  # 0=disconnected, 1=connected
device_reconnect_attempts_total{device_id}
device_messages_published_total{device_id, subject}
device_messages_received_total{device_id, subject}
device_buffer_size{device_id}
device_latency_seconds{device_id}
```

### 4. Application Metrics (Business)

```yaml
# Factory-specific metrics
factory_line_state{line_id}               # 0=stopped, 1=running, 2=fault
factory_oee_availability{line_id}
factory_oee_performance{line_id}
factory_oee_quality{line_id}
factory_oee_overall{line_id}
factory_production_count_total{line_id, product}
factory_defect_count_total{line_id, defect_type}
factory_emergency_stop_total{line_id}
```

## Log Format

All components emit structured JSON logs for Loki ingestion:

```json
{
  "timestamp": "2024-01-15T10:30:00.123Z",
  "level": "info",
  "message": "Device authenticated",
  "service": "gateway",
  "trace_id": "abc123",
  "span_id": "def456",
  "device_id": "temp-sensor-001",
  "device_type": "sensor",
  "remote_addr": "192.168.1.100",
  "duration_ms": 12.5
}
```

### Log Labels (for Loki)

```yaml
# Standard labels
service: gateway | nats | device
environment: production | staging | development
host: hostname

# Gateway-specific
device_id: temp-sensor-001
device_type: sensor | actuator | controller

# Level-based
level: debug | info | warn | error | fatal
```

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              FACTORY FLOOR                                   │
│                                                                             │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐                       │
│   │ Sensor  │  │Conveyor │  │ Vision  │  │ E-Stop  │                       │
│   │  SDK    │  │   SDK   │  │   SDK   │  │   SDK   │                       │
│   └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘                       │
│        │            │            │            │                             │
│        └────────────┼────────────┼────────────┘                             │
│                     │            │                                          │
│                     ▼            ▼                                          │
│              ┌──────────────────────────┐                                   │
│              │     GATEWAY              │                                   │
│              │  ┌────────────────────┐  │                                   │
│              │  │ OpenTelemetry SDK  │  │                                   │
│              │  │ • Metrics          │  │──────┐                            │
│              │  │ • Traces           │  │      │                            │
│              │  │ • Logs (Serilog)   │  │      │                            │
│              │  └────────────────────┘  │      │                            │
│              │           │              │      │                            │
│              │  ┌────────▼───────────┐  │      │                            │
│              │  │ /metrics endpoint  │  │◄─────┼──── Prometheus scrape      │
│              │  └────────────────────┘  │      │                            │
│              └───────────┬──────────────┘      │                            │
│                          │                     │                            │
└──────────────────────────┼─────────────────────┼────────────────────────────┘
                           │                     │
                           ▼                     │
                    ┌─────────────┐              │
                    │    NATS     │              │
                    │  ┌───────┐  │              │
                    │  │/metrics│  │◄────────────┼──── Prometheus scrape
                    │  └───────┘  │              │
                    └─────────────┘              │
                                                │
┌───────────────────────────────────────────────┼─────────────────────────────┐
│                    MONITORING INFRASTRUCTURE  │                              │
│                                               │                              │
│   ┌─────────────┐    ┌─────────────┐         │                              │
│   │ PROMETHEUS  │◄───┤   scrape    │◄────────┘                              │
│   │             │    │  configs    │                                        │
│   │ :9090       │    └─────────────┘                                        │
│   └──────┬──────┘                                                           │
│          │                                                                  │
│   ┌──────▼──────┐    ┌─────────────┐    ┌─────────────┐                     │
│   │   GRAFANA   │◄───┤    LOKI     │◄───┤  Promtail   │◄─── Container logs  │
│   │             │    │             │    │  (or Fluent │                     │
│   │ :3000       │    │ :3100       │    │   Bit)      │                     │
│   └─────────────┘    └─────────────┘    └─────────────┘                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Key Dashboards

### 1. System Overview Dashboard

- Total devices connected
- Messages per second (in/out)
- Active JetStream streams
- System health indicators
- Geographic distribution (if applicable)

### 2. Gateway Performance Dashboard

- Connection rate and active connections
- Authentication success/failure rates
- Message throughput by type
- Latency percentiles (p50, p95, p99)
- Rate limiting events
- Error rates by category

### 3. Device Health Dashboard

- Device connectivity status (per device)
- Reconnection frequency
- Message rates per device
- Buffer utilization
- Last seen timestamps

### 4. Factory Operations Dashboard (Business)

- Line status (running/stopped/fault)
- OEE gauges (Availability, Performance, Quality)
- Production counters
- Defect rates by type
- Alert history
- E-Stop events

### 5. NATS Infrastructure Dashboard

- Cluster health
- Stream statistics
- Consumer lag
- Memory and storage usage
- Connection distribution

## Alerting Rules

### Critical Alerts (Page immediately)

```yaml
# Gateway down
- alert: GatewayDown
  expr: up{job="gateway"} == 0
  for: 1m
  labels:
    severity: critical
  annotations:
    summary: "Gateway is down"

# High error rate
- alert: HighErrorRate
  expr: rate(gateway_messages_sent_total{type="error"}[5m]) > 10
  for: 2m
  labels:
    severity: critical

# JetStream storage full
- alert: JetStreamStorageFull
  expr: nats_jetstream_storage_used / nats_jetstream_storage_limit > 0.9
  for: 5m
  labels:
    severity: critical

# Mass device disconnection
- alert: MassDisconnection
  expr: delta(gateway_websocket_connections_active[5m]) < -100
  for: 1m
  labels:
    severity: critical
```

### Warning Alerts (Notify team)

```yaml
# High authentication failures
- alert: HighAuthFailures
  expr: rate(gateway_auth_attempts_total{status="failure"}[5m]) > 5
  for: 5m
  labels:
    severity: warning

# Consumer lag
- alert: ConsumerLag
  expr: nats_jetstream_consumer_pending > 10000
  for: 10m
  labels:
    severity: warning

# Device offline
- alert: DeviceOffline
  expr: device_connection_state == 0
  for: 15m
  labels:
    severity: warning

# Rate limiting triggered
- alert: RateLimitingActive
  expr: rate(gateway_rate_limit_rejections_total[5m]) > 0
  for: 5m
  labels:
    severity: warning
```

### Info Alerts (Log for review)

```yaml
# Reconnection spike
- alert: ReconnectionSpike
  expr: rate(device_reconnect_attempts_total[5m]) > 10
  for: 5m
  labels:
    severity: info

# New device connected
- alert: NewDeviceConnected
  expr: increase(gateway_auth_attempts_total{status="success"}[1h]) > 0
  for: 0m
  labels:
    severity: info
```

## Implementation Checklist

### Gateway Changes

- [ ] Add `OpenTelemetry.Instrumentation.AspNetCore` package
- [ ] Add `prometheus-net.AspNetCore` package
- [ ] Add `Serilog.Sinks.Loki` package
- [ ] Create custom metrics using `prometheus-net`
- [ ] Add correlation IDs for distributed tracing
- [ ] Configure structured JSON logging
- [ ] Expose `/metrics` endpoint

### SDK Changes

- [ ] Add metrics callback interface
- [ ] Track connection state changes
- [ ] Track message counts and sizes
- [ ] Track reconnection attempts
- [ ] Track buffer utilization
- [ ] Expose statistics via `getStats()` method

### Infrastructure Changes

- [ ] Add Prometheus container
- [ ] Add Loki container
- [ ] Add Grafana container
- [ ] Add Promtail/FluentBit for log collection
- [ ] Configure scrape targets
- [ ] Import dashboards
- [ ] Configure alert rules
- [ ] Set up notification channels

### NATS Changes

- [ ] Enable monitoring port (8222)
- [ ] Enable Prometheus exporter
- [ ] Configure JetStream metrics

## Resource Requirements

### Minimum (Development/Demo)

| Component | CPU | Memory | Storage |
|-----------|-----|--------|---------|
| Prometheus | 0.5 core | 512MB | 10GB |
| Loki | 0.5 core | 256MB | 10GB |
| Grafana | 0.25 core | 256MB | 1GB |
| **Total** | **1.25 cores** | **1GB** | **21GB** |

### Production (100+ devices)

| Component | CPU | Memory | Storage |
|-----------|-----|--------|---------|
| Prometheus | 2 cores | 4GB | 100GB SSD |
| Loki | 2 cores | 2GB | 200GB SSD |
| Grafana | 1 core | 1GB | 10GB |
| **Total** | **5 cores** | **7GB** | **310GB** |

## Retention Policies

| Data Type | Hot Retention | Cold Retention | Archive |
|-----------|---------------|----------------|---------|
| Metrics (Prometheus) | 15 days | - | Thanos/Cortex if needed |
| Logs (Loki) | 7 days | 30 days (compressed) | S3/GCS if required |
| Traces | 7 days | - | - |
| Alerts | 90 days | - | - |

## Security Considerations

1. **Network Isolation**: Monitoring stack in separate network segment
2. **Authentication**: Grafana with LDAP/OAuth integration
3. **Authorization**: Role-based dashboard access
4. **Encryption**: TLS for all monitoring endpoints
5. **Data Sensitivity**: Scrub PII from logs before ingestion
