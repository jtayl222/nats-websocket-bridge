# Monitoring Stack

Complete PLG (Prometheus + Loki + Grafana) monitoring stack for the NATS WebSocket Gateway.

## Quick Start

```bash
# Start the monitoring stack
cd docker/monitoring
docker-compose up -d

# View logs
docker-compose logs -f

# Stop the stack
docker-compose down
```

## Access Points

| Service | URL | Credentials |
|---------|-----|-------------|
| **Grafana** | http://localhost:3000 | admin / admin |
| **Prometheus** | http://localhost:9090 | - |
| **Loki** | http://localhost:3100 | - |
| **Alertmanager** | http://localhost:9093 | - |
| **NATS Monitoring** | http://localhost:8222 | - |
| **NATS Exporter** | http://localhost:7777/metrics | - |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      MONITORING STACK                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐        │
│   │ Prometheus  │    │    Loki     │    │  Grafana    │        │
│   │   :9090     │    │   :3100     │    │   :3000     │        │
│   └──────┬──────┘    └──────┬──────┘    └──────┬──────┘        │
│          │                  │                  │                │
│   ┌──────▼──────┐    ┌──────▼──────┐          │                │
│   │   Scrapes   │    │  Promtail   │          │                │
│   │  /metrics   │    │  (logs)     │          │                │
│   └─────────────┘    └─────────────┘          │                │
│                                               │                │
│   ┌───────────────────────────────────────────▼────────────┐   │
│   │                    Alertmanager                         │   │
│   │                      :9093                              │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Included Dashboards

### 1. Gateway Overview (`gateway-overview`)
- Active connections by device type
- Message rates (in/out) by type
- Processing latency percentiles
- Authentication success/failure rates
- Authorization checks
- Rate limiting events
- Error rates

## Alerting Rules

### Critical Alerts (Page immediately)
- `GatewayDown` - Gateway is unreachable
- `HighErrorRate` - Error rate > 10/sec for 2 minutes
- `MassDisconnection` - 50+ devices disconnected in 5 minutes
- `NATSDown` - NATS server unreachable
- `JetStreamStorageFull` - JetStream storage > 90% full

### Warning Alerts (Team notification)
- `HighAuthFailures` - Auth failure rate > 10%
- `RateLimitingActive` - Devices being rate limited
- `HighProcessingLatency` - p95 latency > 100ms
- `ConsumerLag` - JetStream consumer > 10k pending

### Info Alerts (Logged)
- `ReconnectionSpike` - High reconnection rate
- `AuthorizationDenials` - Unauthorized access attempts

## Configuration

### Enable Loki Logging in Gateway

Edit `appsettings.json`:

```json
{
  "Monitoring": {
    "Loki": {
      "Enabled": true,
      "Url": "http://localhost:3100"
    }
  }
}
```

### Configure Alert Notifications

Edit `alertmanager/alertmanager.yml` to set up:
- Email notifications
- Slack webhooks
- PagerDuty integration

Example Slack configuration:
```yaml
global:
  slack_api_url: 'https://hooks.slack.com/services/xxx/yyy/zzz'

receivers:
  - name: 'critical-receiver'
    slack_configs:
      - channel: '#alerts-critical'
        send_resolved: true
```

### Add Custom Scrape Targets

Edit `prometheus/prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'my-service'
    static_configs:
      - targets: ['myservice:8080']
```

## Prometheus Queries

### Useful PromQL Queries

```promql
# Active connections
sum(gateway_websocket_connections_active)

# Message throughput (messages/sec)
sum(rate(gateway_messages_received_total[5m]))

# Processing latency (p95)
histogram_quantile(0.95, sum(rate(gateway_message_processing_duration_seconds_bucket[5m])) by (le))

# Authentication success rate
sum(rate(gateway_auth_attempts_total{status="success"}[5m])) / sum(rate(gateway_auth_attempts_total[5m]))

# Error rate
sum(rate(gateway_messages_sent_total{type="error"}[5m]))

# NATS publish latency (p95)
histogram_quantile(0.95, sum(rate(gateway_nats_latency_seconds_bucket{operation="publish"}[5m])) by (le))

# Rate limit events
sum by (device_id) (increase(gateway_rate_limit_rejections_total[1h]))
```

## Loki Queries

### Useful LogQL Queries

```logql
# All gateway errors
{service="gateway"} |= "error"

# Authentication failures
{service="gateway"} | json | level="warning" |= "authentication"

# Device connection events
{service="gateway"} |= "connected" or |= "disconnected"

# Rate-limited requests
{service="gateway"} |= "Rate limit"

# Specific device logs
{service="gateway"} | json | device_id="temp-sensor-001"
```

## Troubleshooting

### Prometheus not scraping Gateway
1. Verify Gateway is running: `curl http://localhost:5000/health`
2. Check metrics endpoint: `curl http://localhost:5000/metrics`
3. For Docker on Mac/Windows, ensure `host.docker.internal` resolves

### Loki not receiving logs
1. Check Promtail status: `docker logs promtail`
2. Verify Loki is healthy: `curl http://localhost:3100/ready`
3. Enable Loki sink in Gateway configuration

### Grafana dashboard not loading
1. Check data source connectivity in Grafana
2. Verify Prometheus is scraping: http://localhost:9090/targets
3. Check dashboard JSON syntax if importing custom dashboard

## Resource Requirements

### Development (this setup)
- CPU: 2 cores
- RAM: 2GB
- Storage: 20GB

### Production
- CPU: 4+ cores
- RAM: 8GB+
- Storage: 100GB+ SSD
- Consider external storage for Loki and Prometheus

## Customization

### Adding More Dashboards

1. Create dashboard in Grafana UI
2. Export as JSON
3. Save to `grafana/dashboards/`
4. Dashboards auto-reload every 30 seconds

### Retention Settings

| Service | Default | Configuration |
|---------|---------|---------------|
| Prometheus | 15 days | `--storage.tsdb.retention.time` |
| Loki | 7 days | `limits_config.retention_period` |
| Alertmanager | - | In-memory |
