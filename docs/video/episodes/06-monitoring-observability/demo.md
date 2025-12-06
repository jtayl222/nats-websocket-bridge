# Episode 06: Monitoring & Observability - Demo Script

## Setup

```bash
# Start the full monitoring stack
cd docker/monitoring
docker-compose up -d

# Verify all services are running
docker-compose ps

# Expected output:
# prometheus     Up    0.0.0.0:9090->9090/tcp
# loki          Up    0.0.0.0:3100->3100/tcp
# grafana       Up    0.0.0.0:3000->3000/tcp
# alertmanager  Up    0.0.0.0:9093->9093/tcp
```

---

## Demo 1: Start Gateway with Metrics

```bash
# Terminal 1: Start NATS
docker run -d --name nats -p 4222:4222 nats:latest -js

# Terminal 2: Start Gateway
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Gateway logs show:
# [INF] Prometheus metrics enabled at /metrics
# [INF] Gateway listening on http://0.0.0.0:5000
```

---

## Demo 2: Explore Metrics Endpoint

```bash
# View raw metrics
curl http://localhost:5000/metrics

# Filter specific metrics
curl -s http://localhost:5000/metrics | grep gateway_connections

# Expected output:
# gateway_connections_active 0
# gateway_connections_total{state="connected"} 0
# gateway_connections_total{state="disconnected"} 0
```

---

## Demo 3: Generate Traffic for Metrics

```bash
# Terminal 3: Connect multiple devices
for i in {1..5}; do
  (wscat -c ws://localhost:5000/ws -x '{"type":"AUTH","deviceId":"device-'$i'","credentials":{"apiKey":"test"}}' &)
done

# Check connection metrics
curl -s http://localhost:5000/metrics | grep gateway_connections_active
# gateway_connections_active 5

# Send messages
wscat -c ws://localhost:5000/ws -x '{"type":"AUTH","deviceId":"demo","credentials":{"apiKey":"test"}}' \
  -x '{"type":"PUBLISH","subject":"test.data","payload":{"value":42}}'

# Check message metrics
curl -s http://localhost:5000/metrics | grep gateway_messages
```

---

## Demo 4: Prometheus Targets

```bash
# Open Prometheus UI
open http://localhost:9090

# Navigate to Status → Targets
# Verify gateway target is UP

# Or check via API
curl http://localhost:9090/api/v1/targets | jq '.data.activeTargets[] | {job: .labels.job, health: .health}'
```

---

## Demo 5: Query Metrics in Prometheus

```bash
# Open Prometheus UI
open http://localhost:9090/graph
```

**Queries to demonstrate:**

```promql
# Current active connections
gateway_connections_active

# Messages per second
rate(gateway_messages_received_total[1m])

# P95 publish latency
histogram_quantile(0.95, rate(gateway_nats_publish_duration_seconds_bucket[5m]))

# Error rate
rate(gateway_errors_total[5m])

# Connection success rate
rate(gateway_connections_total{state="connected"}[5m])
/ (rate(gateway_connections_total{state="connected"}[5m]) + rate(gateway_connections_total{state="disconnected"}[5m]))
```

---

## Demo 6: View Gateway Logs

```bash
# Gateway console shows structured JSON logs
# [{"Timestamp":"2024-01-15T10:30:00Z","Level":"Information","MessageTemplate":"Device {DeviceId} connected",...}]

# Logs are also sent to Loki
# Check Loki is receiving
curl http://localhost:3100/ready
```

---

## Demo 7: Loki Log Queries

```bash
# Open Grafana
open http://localhost:3000

# Login: admin / admin

# Go to Explore → Select Loki data source
```

**LogQL queries:**

```logql
# All gateway logs
{service="gateway"}

# Errors only
{service="gateway"} |= "error"

# Parse JSON and filter
{service="gateway"} | json | level="Error"

# Specific device
{service="gateway"} | json | DeviceId="device-1"

# Connection events
{service="gateway"} |= "connected" or |= "disconnected"

# Rate of log entries
rate({service="gateway"}[5m])
```

---

## Demo 8: Import Gateway Dashboard

```bash
# In Grafana:
# 1. Go to Dashboards → Import
# 2. Upload docker/monitoring/dashboards/gateway-overview.json

# Or via API:
curl -X POST http://admin:admin@localhost:3000/api/dashboards/db \
  -H "Content-Type: application/json" \
  -d @docker/monitoring/dashboards/gateway-overview.json
```

---

## Demo 9: Dashboard Walkthrough

```
Open the imported Gateway Overview dashboard

Key panels to highlight:
1. Active Connections (single stat)
2. Messages/sec (graph)
3. P95 Latency (graph)
4. Error Rate (graph)
5. Connections Over Time (graph)
6. Top Devices by Messages (table)
7. Recent Logs (logs panel)
```

---

## Demo 10: Create Custom Panel

```bash
# In Grafana dashboard:
# 1. Click "Add Panel"
# 2. Select "Time series"
# 3. Add query:
```

```promql
# Message throughput by type
sum by (type) (rate(gateway_messages_received_total[1m]))
```

```
# Configure:
# - Title: "Message Types Over Time"
# - Legend: {{type}}
# - Unit: msg/s
```

---

## Demo 11: Create Alert Rule

```bash
# In Grafana:
# 1. Go to Alerting → Alert rules
# 2. Click "New alert rule"
# 3. Configure:
```

**Alert Configuration:**

```yaml
Name: High Connection Drop Rate

Query:
  rate(gateway_connections_total{state="disconnected"}[5m]) > 1

Conditions:
  - When: avg
  - Of: query A
  - Is above: 1

For: 5m

Labels:
  severity: warning

Annotations:
  Summary: "Connection drops exceeding 1/sec"
  Description: "Current rate: {{ $value }}"
```

---

## Demo 12: Trigger an Alert

```bash
# Rapidly connect/disconnect to trigger alert
for i in {1..20}; do
  wscat -c ws://localhost:5000/ws -x '{"type":"AUTH","deviceId":"test-'$i'","credentials":{"apiKey":"test"}}' &
  sleep 0.5
  pkill -f "wscat.*test-$i"
done

# Watch Alertmanager
open http://localhost:9093

# Or check via API
curl http://localhost:9093/api/v2/alerts | jq
```

---

## Demo 13: Alertmanager Configuration

```bash
# Show alertmanager config
cat docker/monitoring/alertmanager/config.yml
```

```yaml
route:
  receiver: 'default'
  group_by: ['alertname']
  group_wait: 30s
  group_interval: 5m
  repeat_interval: 4h
  routes:
    - match:
        severity: critical
      receiver: 'critical'

receivers:
  - name: 'default'
    webhook_configs:
      - url: 'http://webhook-logger:8080/alerts'

  - name: 'critical'
    webhook_configs:
      - url: 'http://webhook-logger:8080/critical'
```

---

## Demo 14: Histogram Deep Dive

```bash
# View histogram buckets
curl -s http://localhost:5000/metrics | grep gateway_nats_publish_duration

# Output shows buckets:
# gateway_nats_publish_duration_seconds_bucket{le="0.001"} 45
# gateway_nats_publish_duration_seconds_bucket{le="0.005"} 120
# gateway_nats_publish_duration_seconds_bucket{le="0.01"} 150
# ...
# gateway_nats_publish_duration_seconds_sum 1.234
# gateway_nats_publish_duration_seconds_count 155
```

**In Prometheus:**
```promql
# Percentage under 10ms
gateway_nats_publish_duration_seconds_bucket{le="0.01"}
/ gateway_nats_publish_duration_seconds_count * 100
```

---

## Demo 15: End-to-End Observability

```bash
# Terminal 1: Watch logs
docker-compose logs -f gateway

# Terminal 2: Watch metrics
watch -n 1 'curl -s http://localhost:5000/metrics | grep gateway_connections'

# Terminal 3: Generate traffic
while true; do
  wscat -c ws://localhost:5000/ws \
    -x '{"type":"AUTH","deviceId":"load-test","credentials":{"apiKey":"test"}}' \
    -x '{"type":"PUBLISH","subject":"test.load","payload":{"i":'$RANDOM'}}'
  sleep 0.1
done

# Observe in Grafana dashboard
```

---

## Cleanup

```bash
# Stop traffic generators
pkill -f wscat

# Stop monitoring stack
cd docker/monitoring
docker-compose down

# Stop NATS
docker stop nats && docker rm nats
```

---

## Troubleshooting

### Prometheus not scraping
```bash
# Check target status
curl http://localhost:9090/api/v1/targets

# Verify metrics endpoint accessible from Prometheus container
docker exec -it prometheus wget -qO- http://host.docker.internal:5000/metrics
```

### Loki not receiving logs
```bash
# Check Loki ready
curl http://localhost:3100/ready

# Verify Serilog Loki sink configured
grep -i loki src/NatsWebSocketBridge.Gateway/appsettings.json
```

### Grafana can't connect to data sources
```bash
# Check data source configuration
curl http://admin:admin@localhost:3000/api/datasources

# Test Prometheus connection
curl http://admin:admin@localhost:3000/api/datasources/proxy/1/api/v1/query?query=up
```
