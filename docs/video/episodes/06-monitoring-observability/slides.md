# Episode 06: Monitoring & Observability - Slides

---

## Slide 1: Title

# Monitoring & Observability
## The PLG Stack for Real-Time Systems

**NATS WebSocket Bridge Series - Episode 06**

---

## Slide 2: Episode Goals

### What You'll Learn

- Three pillars of observability
- Prometheus metrics instrumentation
- Loki log aggregation
- Grafana dashboard design

---

## Slide 3: Why Observability Matters

### You Can't Fix What You Can't See

**Manufacturing Requirements:**
- 99.9% uptime = 8.76 hours downtime/year
- Sub-second incident detection
- Root cause analysis capability
- Compliance audit trails

**Without observability:**
- "Is the system slow or is it the network?"
- "Why did we lose 5 minutes of data?"
- "Which sensor failed first?"

---

## Slide 4: Three Pillars of Observability

```
┌─────────────────────────────────────────────────────────┐
│                    OBSERVABILITY                         │
├─────────────────┬─────────────────┬─────────────────────┤
│                 │                 │                     │
│    METRICS      │     LOGS        │     TRACES          │
│                 │                 │                     │
│  What happened  │  Why it         │  How it             │
│  (numbers)      │  happened       │  happened           │
│                 │  (text)         │  (flow)             │
│                 │                 │                     │
│  Prometheus     │  Loki           │  Jaeger/Tempo       │
│                 │                 │                     │
└─────────────────┴─────────────────┴─────────────────────┘
```

**This episode focuses on Metrics and Logs**

---

## Slide 5: The PLG Stack

```
┌─────────────────────────────────────────────────────────┐
│                     Grafana                              │
│               (Visualization)                            │
│  ┌──────────────────────────────────────────────────┐   │
│  │         Dashboards    │    Alerts                │   │
│  └──────────────────────────────────────────────────┘   │
└───────────────────┬───────────────────┬─────────────────┘
                    │                   │
        ┌───────────┴───────┐   ┌───────┴───────┐
        │                   │   │               │
        ▼                   ▼   ▼               ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│  Prometheus   │   │     Loki      │   │  Alertmanager │
│   (Metrics)   │   │    (Logs)     │   │   (Alerts)    │
└───────┬───────┘   └───────┬───────┘   └───────────────┘
        │                   │
        │ scrape            │ push
        ▼                   ▼
┌─────────────────────────────────────────────────────────┐
│                Gateway / Historian                       │
│            /metrics endpoint    Serilog                  │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 6: Prometheus Metric Types

| Type | Use Case | Example |
|------|----------|---------|
| **Counter** | Ever-increasing values | Messages sent, errors |
| **Gauge** | Values that go up and down | Active connections |
| **Histogram** | Distribution of values | Request latency |
| **Summary** | Similar to histogram, client-side | (less common) |

```csharp
// Counter - only goes up
_messagesTotal = Metrics.CreateCounter(
    "gateway_messages_total", "Total messages processed");

// Gauge - can go up or down
_activeConnections = Metrics.CreateGauge(
    "gateway_connections_active", "Current active connections");

// Histogram - measures distribution
_requestDuration = Metrics.CreateHistogram(
    "gateway_request_duration_seconds", "Request latency");
```

---

## Slide 7: Gateway Metrics Service

```csharp
public class GatewayMetrics : IGatewayMetrics
{
    private readonly Counter _messagesReceived;
    private readonly Counter _messagesSent;
    private readonly Gauge _activeConnections;
    private readonly Histogram _publishLatency;
    private readonly Counter _authAttempts;

    public GatewayMetrics()
    {
        _messagesReceived = Metrics.CreateCounter(
            "gateway_messages_received_total",
            "Messages received from devices",
            new CounterConfiguration {
                LabelNames = new[] { "type" }
            });

        _activeConnections = Metrics.CreateGauge(
            "gateway_connections_active",
            "Current WebSocket connections");

        _publishLatency = Metrics.CreateHistogram(
            "gateway_nats_publish_duration_seconds",
            "NATS publish latency",
            new HistogramConfiguration {
                Buckets = new[] { .001, .005, .01, .025, .05, .1, .25, .5, 1 }
            });
    }
}
```

---

## Slide 8: Key Gateway Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `gateway_connections_active` | Gauge | Current WebSocket connections |
| `gateway_connections_total` | Counter | Total connections (with labels) |
| `gateway_messages_received_total` | Counter | Messages from devices |
| `gateway_messages_sent_total` | Counter | Messages to devices |
| `gateway_nats_publish_duration_seconds` | Histogram | NATS latency |
| `gateway_auth_attempts_total` | Counter | Auth success/failure |
| `gateway_buffer_size` | Gauge | Message buffer usage |
| `gateway_errors_total` | Counter | Errors by type |

---

## Slide 9: Exposing Metrics Endpoint

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Prometheus metrics
builder.Services.AddSingleton<IGatewayMetrics, GatewayMetrics>();

var app = builder.Build();

// Expose /metrics endpoint
app.UseMetricServer();
// Or at custom path:
app.UseMetricServer("/internal/metrics");

app.Run();
```

**Prometheus scrapes this endpoint:**
```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'gateway'
    static_configs:
      - targets: ['gateway:5000']
    metrics_path: /metrics
    scrape_interval: 15s
```

---

## Slide 10: Structured Logging with Serilog

```csharp
// Program.cs
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Gateway")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.GrafanaLoki("http://loki:3100")
    .CreateLogger();

// Usage in code
_logger.LogInformation(
    "Device {DeviceId} connected from {ClientIp}",
    deviceId, clientIp);

// Produces JSON:
{
    "Timestamp": "2024-01-15T10:30:00Z",
    "Level": "Information",
    "Message": "Device SENSOR-001 connected from 10.0.1.50",
    "Properties": {
        "DeviceId": "SENSOR-001",
        "ClientIp": "10.0.1.50",
        "Service": "Gateway"
    }
}
```

---

## Slide 11: Log Levels Strategy

| Level | Use For | Example |
|-------|---------|---------|
| **Verbose** | Detailed debugging | Message bytes received |
| **Debug** | Development info | Message routing decision |
| **Information** | Normal operation | Device connected |
| **Warning** | Recoverable issues | Reconnection attempt |
| **Error** | Failures | Authentication failed |
| **Fatal** | Application crash | Unhandled exception |

```csharp
// Good log messages include context
_logger.LogWarning(
    "Device {DeviceId} auth failed: {Reason}. Attempt {Attempt}/{MaxAttempts}",
    deviceId, reason, attempt, maxAttempts);

// Bad - missing context
_logger.LogWarning("Auth failed");
```

---

## Slide 12: Loki Log Queries (LogQL)

```logql
# All logs from gateway
{service="gateway"}

# Errors only
{service="gateway"} |= "error"

# JSON parsing with jq-style
{service="gateway"} | json | level="Error"

# Filter by device
{service="gateway"} | json | DeviceId="SENSOR-001"

# Rate of errors over time
rate({service="gateway"} |= "error" [5m])

# Top devices by log volume
topk(10, sum by (DeviceId) (
    rate({service="gateway"} | json [5m])
))
```

---

## Slide 13: Docker Compose - Monitoring Stack

```yaml
# docker/monitoring/docker-compose.yml
services:
  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'

  loki:
    image: grafana/loki:latest
    ports:
      - "3100:3100"

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - ./dashboards:/etc/grafana/provisioning/dashboards
      - ./datasources.yml:/etc/grafana/provisioning/datasources/default.yml
```

---

## Slide 14: Grafana Dashboard Design

```
┌─────────────────────────────────────────────────────────┐
│                   Gateway Overview                       │
├─────────────────────────────────────────────────────────┤
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐       │
│  │  Active │ │ Msg/sec │ │  Errors │ │ Latency │       │
│  │   142   │ │  1,247  │ │    3    │ │  12ms   │       │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘       │
├─────────────────────────────────────────────────────────┤
│  Connections Over Time          │  Message Throughput   │
│  ┌─────────────────────────┐   │  ┌─────────────────┐  │
│  │    ╱╲    ╱╲             │   │  │    ───────────  │  │
│  │   ╱  ╲  ╱  ╲    ╱╲      │   │  │   ╱           ╲ │  │
│  │  ╱    ╲╱    ╲  ╱  ╲     │   │  │  ╱             ╲│  │
│  └─────────────────────────┘   │  └─────────────────┘  │
├─────────────────────────────────────────────────────────┤
│  Recent Logs                                            │
│  ┌─────────────────────────────────────────────────┐   │
│  │ 10:30:00 INFO  Device SENSOR-001 connected      │   │
│  │ 10:30:01 INFO  Published telemetry.sensor.temp  │   │
│  │ 10:30:02 WARN  Device SENSOR-002 reconnecting   │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 15: Key Dashboard Panels

### Connection Health
```promql
# Active connections gauge
gateway_connections_active

# Connection rate
rate(gateway_connections_total[5m])

# Disconnection rate
rate(gateway_connections_total{state="disconnected"}[5m])
```

### Message Throughput
```promql
# Messages per second
rate(gateway_messages_received_total[1m])

# By message type
sum by (type) (rate(gateway_messages_received_total[1m]))
```

---

## Slide 16: Latency Panels

### Histogram Percentiles

```promql
# P50 latency
histogram_quantile(0.50,
    rate(gateway_nats_publish_duration_seconds_bucket[5m]))

# P95 latency
histogram_quantile(0.95,
    rate(gateway_nats_publish_duration_seconds_bucket[5m]))

# P99 latency
histogram_quantile(0.99,
    rate(gateway_nats_publish_duration_seconds_bucket[5m]))
```

**Why percentiles matter:**
- Average hides outliers
- P99 shows real user experience
- P50 shows typical case

---

## Slide 17: Alerting Rules

```yaml
# prometheus/alerts.yml
groups:
  - name: gateway
    rules:
      - alert: HighConnectionDropRate
        expr: |
          rate(gateway_connections_total{state="disconnected"}[5m])
          / rate(gateway_connections_total{state="connected"}[5m])
          > 0.1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High connection drop rate"

      - alert: HighPublishLatency
        expr: |
          histogram_quantile(0.95,
            rate(gateway_nats_publish_duration_seconds_bucket[5m]))
          > 0.5
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "P95 publish latency exceeds 500ms"

      - alert: NoActiveConnections
        expr: gateway_connections_active == 0
        for: 5m
        labels:
          severity: critical
```

---

## Slide 18: Alert Routing

```yaml
# alertmanager/config.yml
route:
  receiver: 'default'
  group_by: ['alertname', 'severity']
  routes:
    - match:
        severity: critical
      receiver: 'pagerduty'
    - match:
        severity: warning
      receiver: 'slack'

receivers:
  - name: 'default'
    email_configs:
      - to: 'team@company.com'

  - name: 'pagerduty'
    pagerduty_configs:
      - service_key: '<key>'

  - name: 'slack'
    slack_configs:
      - channel: '#alerts'
        api_url: 'https://hooks.slack.com/...'
```

---

## Slide 19: Runbook Integration

### Link Alerts to Documentation

```yaml
- alert: HighPublishLatency
  annotations:
    summary: "P95 publish latency exceeds 500ms"
    description: |
      The 95th percentile publish latency is {{ $value }}s.
      This indicates NATS or network issues.
    runbook_url: "https://wiki.company.com/runbooks/gateway-latency"
    dashboard_url: "https://grafana.company.com/d/gateway"
```

**Runbook should include:**
1. What the alert means
2. Immediate actions to take
3. Escalation path
4. Historical context

---

## Slide 20: Next Episode Preview

# Episode 07: Historical Data Retention

- FDA 21 CFR Part 11 compliance
- Three-tier storage architecture
- TimescaleDB for time-series
- Audit trail implementation

**See you in the next episode!**
