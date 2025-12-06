# Historical Data Retention Architecture

## Executive Summary

This document describes the data retention architecture for PharmaCo's manufacturing environment, designed to meet FDA 21 CFR Part 11 requirements and enable historical troubleshooting of manufacturing issues.

## Compliance Requirements

### FDA 21 CFR Part 11
- **Audit trails**: Record who, what, when for all data changes
- **Data integrity**: Ensure data cannot be altered without detection
- **Electronic signatures**: Authenticated user actions
- **Record retention**: Maintain records for required periods

### GxP Requirements
- **Batch records**: Complete manufacturing history per batch
- **Deviation investigation**: Ability to reconstruct events
- **Data retention periods**:
  - Batch records: Life of product + 1 year (often 10+ years)
  - Equipment logs: 5-7 years minimum
  - Calibration records: Life of equipment

### ALCOA+ Principles
- **A**ttributable - Who performed the action
- **L**egible - Readable throughout retention period
- **C**ontemporaneous - Recorded at time of activity
- **O**riginal - First capture of data
- **A**ccurate - Error-free, truthful
- **+** Complete, Consistent, Enduring, Available

## Data Retention Tiers

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        DATA RETENTION ARCHITECTURE                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         HOT TIER (0-7 days)                          │   │
│  │                                                                      │   │
│  │   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐             │   │
│  │   │  JetStream  │    │ Prometheus  │    │    Loki     │             │   │
│  │   │             │    │             │    │             │             │   │
│  │   │ Real-time   │    │  Metrics    │    │    Logs     │             │   │
│  │   │ Messages    │    │  15 days    │    │   7 days    │             │   │
│  │   └─────────────┘    └─────────────┘    └─────────────┘             │   │
│  │                                                                      │   │
│  │   • Immediate queries    • Dashboards    • Troubleshooting          │   │
│  │   • Real-time alerts     • Alerting      • Recent events            │   │
│  └──────────────────────────────┬───────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        WARM TIER (7 days - 1 year)                   │   │
│  │                                                                      │   │
│  │   ┌─────────────────────────────────────────────────────────────┐   │   │
│  │   │                      TimescaleDB                             │   │   │
│  │   │                                                              │   │   │
│  │   │  • Continuous aggregates (hourly, daily rollups)             │   │   │
│  │   │  • Full resolution for recent data                           │   │   │
│  │   │  • Automated compression                                     │   │   │
│  │   │  • SQL query interface                                       │   │   │
│  │   │  • Retention: 1 year full, 5 years aggregated                │   │   │
│  │   └─────────────────────────────────────────────────────────────┘   │   │
│  │                                                                      │   │
│  │   • Batch investigation    • Trend analysis    • Reports            │   │
│  └──────────────────────────────┬───────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        COLD TIER (1+ years)                          │   │
│  │                                                                      │   │
│  │   ┌─────────────────────────────────────────────────────────────┐   │   │
│  │   │                    Object Storage (S3/MinIO)                 │   │   │
│  │   │                                                              │   │   │
│  │   │  • Parquet files (compressed, columnar)                      │   │   │
│  │   │  • Organized by: /year/month/batch_id/data_type/             │   │   │
│  │   │  • Immutable (WORM - Write Once Read Many)                   │   │   │
│  │   │  • Checksums for integrity verification                      │   │   │
│  │   │  • Retention: 10+ years (configurable)                       │   │   │
│  │   └─────────────────────────────────────────────────────────────┘   │   │
│  │                                                                      │   │
│  │   • Regulatory audits    • Legal holds    • Long-term compliance    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Data Categories and Retention

| Data Type | Hot (JetStream) | Warm (TimescaleDB) | Cold (S3) | Total Retention |
|-----------|-----------------|--------------------|-----------| ----------------|
| **Telemetry** (temp, pressure) | 24 hours | 1 year (full) + 5 years (hourly) | 10 years (daily) | 10 years |
| **Events** (state changes) | 7 days | 2 years (full) | 10 years | 10 years |
| **Alerts** (critical) | 30 days | 5 years (full) | 15 years | 15 years |
| **Quality Data** (inspections) | 30 days | Batch life + 1 year | 15 years | 15 years |
| **Batch Records** | 7 days | Batch life + 1 year | Life + 3 years | Product lifecycle |
| **Audit Logs** | 7 days | 7 years | Indefinite | Indefinite |
| **Device Connectivity** | 24 hours | 90 days | 2 years | 2 years |
| **Commands** | 7 days | 1 year | 10 years | 10 years |

## Architecture Components

### 1. TimescaleDB Historian

Primary warm storage for queryable historical data.

```sql
-- Schema for historical telemetry
CREATE TABLE telemetry (
    time        TIMESTAMPTZ NOT NULL,
    device_id   TEXT NOT NULL,
    line_id     TEXT NOT NULL,
    batch_id    TEXT,
    metric_name TEXT NOT NULL,
    value       DOUBLE PRECISION NOT NULL,
    unit        TEXT,
    quality     INTEGER DEFAULT 192,  -- OPC quality code

    -- Audit fields
    received_at TIMESTAMPTZ DEFAULT NOW(),
    source_ip   INET
);

SELECT create_hypertable('telemetry', 'time');

-- Continuous aggregate for hourly rollups
CREATE MATERIALIZED VIEW telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS hour,
    device_id,
    line_id,
    batch_id,
    metric_name,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    COUNT(*) as sample_count
FROM telemetry
GROUP BY hour, device_id, line_id, batch_id, metric_name;

-- Retention policies
SELECT add_retention_policy('telemetry', INTERVAL '1 year');
SELECT add_retention_policy('telemetry_hourly', INTERVAL '5 years');

-- Compression (after 7 days)
ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, line_id'
);
SELECT add_compression_policy('telemetry', INTERVAL '7 days');
```

### 2. Event Store

Complete event history with immutable audit trail.

```sql
-- Events table
CREATE TABLE events (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    time            TIMESTAMPTZ NOT NULL,
    event_type      TEXT NOT NULL,
    device_id       TEXT NOT NULL,
    line_id         TEXT NOT NULL,
    batch_id        TEXT,
    payload         JSONB NOT NULL,

    -- Audit fields
    correlation_id  UUID,
    causation_id    UUID,
    user_id         TEXT,
    received_at     TIMESTAMPTZ DEFAULT NOW(),

    -- Integrity
    checksum        TEXT NOT NULL,
    previous_hash   TEXT  -- Chain for tamper detection
);

SELECT create_hypertable('events', 'time');

-- Index for batch investigation
CREATE INDEX idx_events_batch ON events (batch_id, time DESC);
CREATE INDEX idx_events_device ON events (device_id, time DESC);
CREATE INDEX idx_events_type ON events (event_type, time DESC);
```

### 3. Audit Log (Immutable)

Compliance-grade audit trail.

```sql
-- Audit log - append only, no updates or deletes
CREATE TABLE audit_log (
    id              BIGSERIAL PRIMARY KEY,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Who
    user_id         TEXT,
    device_id       TEXT,
    source_ip       INET,

    -- What
    action          TEXT NOT NULL,  -- CREATE, READ, UPDATE, DELETE, LOGIN, etc.
    resource_type   TEXT NOT NULL,  -- batch, device, configuration, etc.
    resource_id     TEXT,

    -- Details
    old_value       JSONB,
    new_value       JSONB,
    metadata        JSONB,

    -- Integrity chain
    checksum        TEXT NOT NULL,
    previous_hash   TEXT NOT NULL
);

-- Prevent modifications
REVOKE UPDATE, DELETE ON audit_log FROM PUBLIC;

-- Trigger to enforce immutability
CREATE OR REPLACE FUNCTION prevent_audit_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Audit log modifications are not permitted';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_immutable
BEFORE UPDATE OR DELETE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_audit_modification();
```

### 4. Cold Storage Archival

Long-term archival to object storage.

```
s3://pharmaco-historical-data/
├── telemetry/
│   ├── year=2024/
│   │   ├── month=01/
│   │   │   ├── line_id=line1/
│   │   │   │   ├── telemetry_20240101.parquet
│   │   │   │   ├── telemetry_20240102.parquet
│   │   │   │   └── ...
│   │   │   └── _manifest.json
│   │   └── month=02/
│   └── year=2025/
├── events/
│   ├── year=2024/
│   │   └── batch_id=BATCH-2024-001/
│   │       ├── events.parquet
│   │       └── _checksum.sha256
├── batches/
│   └── BATCH-2024-001/
│       ├── batch_record.json
│       ├── telemetry_summary.parquet
│       ├── events.parquet
│       ├── quality_results.parquet
│       ├── deviations.json
│       └── _signature.p7s  # Digital signature
└── audit/
    └── year=2024/
        └── month=01/
            └── audit_log.parquet.gz
```

## Data Flow Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DATA FLOW ARCHITECTURE                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────┐                                                               │
│  │ Devices  │                                                               │
│  └────┬─────┘                                                               │
│       │                                                                     │
│       ▼                                                                     │
│  ┌──────────┐     ┌─────────────────────────────────────────────────────┐  │
│  │ Gateway  │────►│                    NATS + JetStream                  │  │
│  └──────────┘     │                                                      │  │
│                   │  TELEMETRY stream ──┬──► Prometheus (metrics)        │  │
│                   │  EVENTS stream ─────┼──► Loki (logs)                 │  │
│                   │  ALERTS stream ─────┘                                │  │
│                   │                      │                               │  │
│                   └──────────────────────┼───────────────────────────────┘  │
│                                          │                                  │
│                                          ▼                                  │
│                   ┌──────────────────────────────────────────────────────┐  │
│                   │              Historian Service                        │  │
│                   │                                                       │  │
│                   │  ┌─────────────────────────────────────────────────┐ │  │
│                   │  │           JetStream Consumers                    │ │  │
│                   │  │                                                  │ │  │
│                   │  │  • telemetry-historian (durable)                 │ │  │
│                   │  │  • events-historian (durable)                    │ │  │
│                   │  │  • alerts-historian (durable)                    │ │  │
│                   │  └────────────────────┬────────────────────────────┘ │  │
│                   │                       │                              │  │
│                   │                       ▼                              │  │
│                   │  ┌─────────────────────────────────────────────────┐ │  │
│                   │  │              Data Processing                     │ │  │
│                   │  │                                                  │ │  │
│                   │  │  • Validate & enrich data                        │ │  │
│                   │  │  • Calculate checksums                           │ │  │
│                   │  │  • Associate with batch                          │ │  │
│                   │  │  • Write audit log entry                         │ │  │
│                   │  └────────────────────┬────────────────────────────┘ │  │
│                   │                       │                              │  │
│                   └───────────────────────┼──────────────────────────────┘  │
│                                           │                                 │
│                     ┌─────────────────────┼─────────────────────┐           │
│                     │                     │                     │           │
│                     ▼                     ▼                     ▼           │
│              ┌─────────────┐      ┌─────────────┐      ┌─────────────┐     │
│              │ TimescaleDB │      │  Audit Log  │      │   Batch     │     │
│              │             │      │  (immutable)│      │  Context    │     │
│              │ • Telemetry │      │             │      │  Service    │     │
│              │ • Events    │      │             │      │             │     │
│              │ • Quality   │      │             │      │             │     │
│              └──────┬──────┘      └─────────────┘      └─────────────┘     │
│                     │                                                       │
│                     │  (scheduled archival)                                 │
│                     ▼                                                       │
│              ┌─────────────────────────────────────────────────────────┐   │
│              │                   Archival Service                       │   │
│              │                                                          │   │
│              │  • Export to Parquet                                     │   │
│              │  • Generate checksums                                    │   │
│              │  • Upload to S3/MinIO                                    │   │
│              │  • Verify integrity                                      │   │
│              │  • Update manifest                                       │   │
│              └──────────────────────────┬──────────────────────────────┘   │
│                                         │                                   │
│                                         ▼                                   │
│              ┌─────────────────────────────────────────────────────────┐   │
│              │                    S3/MinIO                              │   │
│              │                (WORM-enabled bucket)                     │   │
│              │                                                          │   │
│              │  • Object Lock (compliance mode)                         │   │
│              │  • Versioning enabled                                    │   │
│              │  • Cross-region replication                              │   │
│              │  • Lifecycle policies                                    │   │
│              └─────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Batch Investigation Workflow

When a manufacturing issue is discovered:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    BATCH INVESTIGATION WORKFLOW                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. IDENTIFY AFFECTED BATCH                                                 │
│     └─► Query: "What batches were produced between date X and Y?"           │
│                                                                             │
│  2. RETRIEVE BATCH CONTEXT                                                  │
│     ├─► Batch record (product, lot, dates, operators)                       │
│     ├─► Equipment used                                                      │
│     └─► Environmental conditions                                            │
│                                                                             │
│  3. RECONSTRUCT TIMELINE                                                    │
│     ├─► All events during batch production                                  │
│     ├─► State changes (line start/stop, speed changes)                      │
│     ├─► Alerts and alarms                                                   │
│     └─► Operator actions                                                    │
│                                                                             │
│  4. ANALYZE TELEMETRY                                                       │
│     ├─► Temperature profiles (was it in spec?)                              │
│     ├─► Process parameters                                                  │
│     ├─► Equipment performance                                               │
│     └─► Anomaly detection                                                   │
│                                                                             │
│  5. REVIEW QUALITY DATA                                                     │
│     ├─► Inspection results                                                  │
│     ├─► Defect rates                                                        │
│     └─► OEE during production                                               │
│                                                                             │
│  6. GENERATE INVESTIGATION REPORT                                           │
│     ├─► Timeline visualization                                              │
│     ├─► Data exports (signed, verified)                                     │
│     └─► Audit trail of investigation                                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Query Examples

### Find all data for a specific batch

```sql
-- Get batch context
SELECT * FROM batches WHERE batch_id = 'BATCH-2024-001';

-- Get all events during batch production
SELECT * FROM events
WHERE batch_id = 'BATCH-2024-001'
ORDER BY time;

-- Get telemetry with anomalies highlighted
SELECT
    time,
    device_id,
    metric_name,
    value,
    CASE
        WHEN metric_name = 'temperature' AND (value < 20 OR value > 25) THEN 'OUT_OF_SPEC'
        ELSE 'OK'
    END as status
FROM telemetry
WHERE batch_id = 'BATCH-2024-001'
AND metric_name = 'temperature'
ORDER BY time;

-- Get hourly aggregates for trend analysis
SELECT * FROM telemetry_hourly
WHERE batch_id = 'BATCH-2024-001'
ORDER BY hour;
```

### Find batches with temperature excursions

```sql
SELECT DISTINCT batch_id,
       MIN(value) as min_temp,
       MAX(value) as max_temp,
       COUNT(*) as excursion_count
FROM telemetry
WHERE metric_name = 'temperature'
AND (value < 20 OR value > 25)  -- Out of spec
AND time > NOW() - INTERVAL '30 days'
GROUP BY batch_id
ORDER BY excursion_count DESC;
```

### Reconstruct device state at a point in time

```sql
-- Get the last known state of all devices at a specific time
SELECT DISTINCT ON (device_id)
    device_id,
    time,
    payload->>'state' as state,
    payload
FROM events
WHERE event_type = 'state_change'
AND time <= '2024-01-15 14:30:00'
ORDER BY device_id, time DESC;
```

### Audit trail for a batch

```sql
SELECT
    timestamp,
    user_id,
    device_id,
    action,
    resource_type,
    old_value,
    new_value
FROM audit_log
WHERE metadata->>'batch_id' = 'BATCH-2024-001'
ORDER BY timestamp;
```

## Data Integrity Verification

### Checksum Chain

Each record includes a checksum linked to the previous record:

```python
def calculate_checksum(record, previous_hash):
    """Calculate integrity checksum for a record."""
    import hashlib
    import json

    # Create deterministic JSON representation
    content = json.dumps(record, sort_keys=True, default=str)

    # Include previous hash in chain
    to_hash = f"{previous_hash}:{content}"

    return hashlib.sha256(to_hash.encode()).hexdigest()
```

### Verification Query

```sql
-- Verify audit log integrity
WITH ordered_logs AS (
    SELECT
        id,
        checksum,
        previous_hash,
        LAG(checksum) OVER (ORDER BY id) as expected_previous
    FROM audit_log
    ORDER BY id
)
SELECT
    id,
    CASE
        WHEN previous_hash = expected_previous THEN 'VALID'
        WHEN expected_previous IS NULL THEN 'GENESIS'
        ELSE 'TAMPERED'
    END as integrity_status
FROM ordered_logs
WHERE previous_hash != expected_previous
AND expected_previous IS NOT NULL;
```

## Implementation Checklist

### Phase 1: Foundation (Week 1-2)
- [ ] Deploy TimescaleDB
- [ ] Create schema and hypertables
- [ ] Set up JetStream consumers for historian
- [ ] Implement basic data ingestion

### Phase 2: Warm Storage (Week 3-4)
- [ ] Implement continuous aggregates
- [ ] Set up compression policies
- [ ] Create retention policies
- [ ] Build query APIs

### Phase 3: Cold Storage (Week 5-6)
- [ ] Deploy MinIO/S3
- [ ] Implement archival service
- [ ] Create Parquet export jobs
- [ ] Set up WORM policies

### Phase 4: Compliance (Week 7-8)
- [ ] Implement audit logging
- [ ] Add checksum chains
- [ ] Create integrity verification tools
- [ ] Build investigation workflows

### Phase 5: Validation (Week 9-10)
- [ ] IQ/OQ/PQ documentation
- [ ] Validation test scripts
- [ ] Disaster recovery testing
- [ ] Compliance audit readiness
