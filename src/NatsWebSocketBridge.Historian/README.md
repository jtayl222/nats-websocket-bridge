# NATS WebSocket Bridge Historian

A compliance-grade historical data retention service for pharmaceutical manufacturing environments. Implements FDA 21 CFR Part 11 requirements for electronic records.

## Overview

The Historian service provides:

- **JetStream Consumption**: Subscribes to NATS JetStream streams (TELEMETRY, EVENTS, ALERTS, QUALITY)
- **TimescaleDB Persistence**: Stores time-series data in hypertables with automatic compression
- **Audit Logging**: Immutable, tamper-evident audit trail with checksum chains
- **Cold Storage Archival**: Archives old data to S3/MinIO for long-term retention
- **Prometheus Metrics**: Full observability for monitoring and alerting

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   JetStream     │────▶│   Historian     │────▶│  TimescaleDB    │
│  (Hot: 7 days)  │     │    Service      │     │ (Warm: 1 year)  │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                                │
                                ▼
                        ┌─────────────────┐
                        │   S3/MinIO      │
                        │ (Cold: 10 years)│
                        └─────────────────┘
```

## Data Retention Tiers

| Tier | Storage | Retention | Data Types |
|------|---------|-----------|------------|
| Hot | JetStream | 7 days | Real-time telemetry |
| Warm | TimescaleDB | 1 year | Historical queries |
| Cold | S3/MinIO | 10+ years | Compliance archives |

## Compliance Features

### FDA 21 CFR Part 11

- **Attributable**: All records include user/device ID, timestamp, source IP
- **Legible**: Data stored in structured, queryable format
- **Contemporaneous**: Records captured at time of event
- **Original**: Checksums verify data integrity
- **Accurate**: Validation on ingestion, immutable storage

### Audit Trail

- Immutable audit log table with triggers preventing modification
- Cryptographic hash chain linking all entries
- Automatic integrity verification
- Records: CREATE, READ, UPDATE, DELETE, EXPORT, INGEST, ARCHIVE

## Quick Start

### Using Docker Compose

```bash
# Start the full historian stack
cd docker/historian
docker-compose up -d

# View logs
docker-compose logs -f historian

# Access pgAdmin at http://localhost:5050
# Access MinIO console at http://localhost:9001
```

### Running Locally

```bash
# Prerequisites: NATS with JetStream, TimescaleDB

# Restore and build
dotnet restore
dotnet build

# Run
dotnet run --project src/NatsWebSocketBridge.Historian
```

## Configuration

### appsettings.json

```json
{
  "Historian": {
    "ConnectionString": "Host=localhost;Port=5432;Database=manufacturing_history;Username=historian;Password=...",
    "BatchSize": 100,
    "BatchTimeoutMs": 1000,
    "EnableAuditLogging": true,
    "EnableIntegrityChecks": true
  },
  "Nats": {
    "Url": "nats://localhost:4222",
    "ClientName": "historian-service"
  },
  "JetStream": {
    "DefaultBatchSize": 100,
    "FetchTimeout": "5s",
    "AckWait": "60s",
    "MaxDeliver": 5,
    "Consumers": [...]
  },
  "Archival": {
    "Enabled": true,
    "S3Endpoint": "http://localhost:9000",
    "TelemetryArchiveAgeDays": 90,
    "ArchiveIntervalHours": 24
  }
}
```

## Database Schema

### Hypertables

- **telemetry**: High-frequency sensor data
- **events**: State changes, alerts, commands
- **quality_inspections**: Vision scanner results
- **batches**: Master batch records
- **audit_log**: Immutable audit trail

### Continuous Aggregates

- **telemetry_hourly**: Pre-computed hourly rollups
- **telemetry_daily**: Pre-computed daily rollups

### Retention Policies

| Table | Retention | Compression |
|-------|-----------|-------------|
| telemetry | 1 year | After 7 days |
| events | 2 years | After 30 days |
| quality_inspections | 5 years | After 30 days |
| audit_log | 7 years | After 90 days |

## Prometheus Metrics

| Metric | Description |
|--------|-------------|
| `historian_messages_processed_total` | Messages processed by data type |
| `historian_records_inserted_total` | Records inserted by table |
| `historian_processing_latency_seconds` | Processing latency histogram |
| `historian_queue_depth` | Current queue depth by data type |
| `historian_audit_entries_total` | Audit log entries created |
| `historian_archives_created_total` | Archive files created |

Metrics endpoint: `http://localhost:9091/metrics`

## API Reference

### Audit Log Verification

```csharp
// Verify audit log integrity
var result = await auditLogService.VerifyIntegrityAsync();

if (!result.IsValid)
{
    Console.WriteLine($"Tampered records: {result.TamperedRecords}");
}
```

### Historical Queries

```csharp
// Query telemetry for a device
var records = await repository.GetTelemetryAsync(
    deviceId: "sensor-001",
    start: DateTimeOffset.UtcNow.AddDays(-7),
    end: DateTimeOffset.UtcNow,
    metricName: "temperature"
);

// Query events for a batch
var events = await repository.GetEventsByBatchAsync("BATCH-2024-001");
```

## Troubleshooting

### Common Issues

1. **TimescaleDB connection fails**
   - Ensure TimescaleDB is running and accessible
   - Verify connection string credentials
   - Check that `timescaledb` extension is installed

2. **JetStream streams not found**
   - Ensure NATS server has JetStream enabled
   - Verify streams are created by the Gateway
   - Check consumer filter subjects match stream subjects

3. **Audit log integrity verification fails**
   - Check for database tampering
   - Verify no direct database modifications occurred
   - Review audit log for gaps in sequence

### Logs

The service uses Serilog with JSON formatting for Loki ingestion:

```bash
# View structured logs
docker logs historian-service | jq
```

## License

See the root LICENSE file.
