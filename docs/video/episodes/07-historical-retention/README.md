# Episode 07: Historical Data Retention

**Duration:** 15-18 minutes
**Prerequisites:** [Episode 01](../01-intro/README.md) through [Episode 03](../03-gateway-architecture/README.md)
**Series:** [NATS WebSocket Bridge Video Series](../../SERIES_OVERVIEW.md) (7 of 7)

> **Technical Reference:** For complete schema definitions, SQL examples, and implementation checklist, see [Historical Data Retention Architecture](../../../compliance/HISTORICAL_DATA_RETENTION.md).

## Learning Objectives

By the end of this episode, viewers will understand:
- FDA 21 CFR Part 11 compliance requirements for electronic records
- Three-tier retention architecture (Hot/Warm/Cold)
- TimescaleDB hypertables and continuous aggregates for pharmaceutical data
- Audit trail implementation with tamper-detection hash chains

## Outline

1. **The Compliance Challenge** (0:00-2:00)
   - FDA 21 CFR Part 11 overview
   - ALCOA+ principles
   - "What happened 3 weeks ago?"
   - Data integrity requirements

2. **Three-Tier Architecture** (2:00-4:00)
   - Hot: JetStream (7 days)
   - Warm: TimescaleDB (1 year)
   - Cold: S3/MinIO (10+ years)
   - Data flow between tiers

3. **TimescaleDB Setup** (4:00-7:00)
   - Why TimescaleDB over PostgreSQL
   - Hypertables for time-series
   - Compression policies
   - Retention policies
   - Demo: Create hypertable

4. **Historian Service** (7:00-10:00)
   - JetStream consumer
   - Batch inserts to TimescaleDB
   - Checksum computation
   - Service architecture
   - Demo: Data flowing

5. **Continuous Aggregates** (10:00-12:00)
   - Pre-computed rollups
   - Hourly and daily aggregates
   - Query performance benefits
   - Demo: Query aggregates

6. **Audit Trail** (12:00-15:00)
   - Immutable audit_log table
   - Hash chain for tamper detection
   - Trigger-based protection
   - Integrity verification
   - Demo: Audit log

7. **Cold Storage Archival** (15:00-17:00)
   - MinIO/S3 setup
   - Archive service
   - Compressed JSON export
   - Retention lifecycle
   - Demo: Archive creation

## Database Schema Highlights

```sql
-- Telemetry hypertable
CREATE TABLE telemetry (
    time TIMESTAMPTZ NOT NULL,
    device_id TEXT NOT NULL,
    metric_name TEXT NOT NULL,
    value DOUBLE PRECISION NOT NULL,
    checksum TEXT NOT NULL
);
SELECT create_hypertable('telemetry', 'time');

-- Continuous aggregate
CREATE MATERIALIZED VIEW telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS bucket,
    device_id, metric_name,
    AVG(value), MIN(value), MAX(value)
FROM telemetry
GROUP BY bucket, device_id, metric_name;

-- Immutable audit log
CREATE TABLE audit_log (...);
CREATE TRIGGER audit_immutable_update
BEFORE UPDATE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_modification();
```

## Demo Commands

```bash
# Start historian stack
docker-compose -f docker/historian/docker-compose.yml up -d

# Connect to TimescaleDB
psql -h localhost -U historian manufacturing_history

# Query recent telemetry
SELECT * FROM telemetry
WHERE device_id = 'SENSOR-001'
  AND time > NOW() - INTERVAL '1 hour';

# Query hourly aggregates
SELECT * FROM telemetry_hourly
WHERE bucket > NOW() - INTERVAL '24 hours';

# Verify audit integrity
SELECT * FROM verify_audit_integrity();
```

## Key Visuals

- Three-tier architecture diagram
- Data lifecycle flow
- Hypertable chunking visualization
- Audit hash chain diagram
- Retention policy timeline

## Compliance Checklist

- [ ] All data attributed (device_id, timestamp)
- [ ] Original records preserved (checksums)
- [ ] Contemporaneous capture (real-time ingestion)
- [ ] Audit trail immutable
- [ ] Retention policies documented
- [ ] Integrity verification available

## ALCOA+ Principles in Practice

This architecture implements all ALCOA+ data integrity principles:

| Principle | Implementation | Component |
|-----------|----------------|-----------|
| **Attributable** | `device_id`, `user_id` on every record | Historian Service |
| **Legible** | Structured JSON, SQL-queryable | TimescaleDB |
| **Contemporaneous** | Device-side timestamps, real-time ingestion | Device SDK, JetStream |
| **Original** | Immutable audit log, checksums | Audit Log Service |
| **Accurate** | Validation on ingestion, integrity checks | Gateway, Historian |
| **Complete** | Full batch records, no gaps | Batch Context Service |
| **Consistent** | Cross-system correlation IDs | All components |
| **Enduring** | 10+ year retention, Parquet archives | S3/MinIO Cold Storage |
| **Available** | SQL queries, Grafana dashboards | TimescaleDB, Grafana |

## Batch Investigation Workflow

When Quality Assurance identifies a potential issue with batch B2024-001:

```sql
-- 1. Get batch context
SELECT * FROM batches WHERE batch_id = 'B2024-001';

-- 2. Find all temperature readings during production
SELECT time, device_id, value, quality
FROM telemetry
WHERE batch_id = 'B2024-001'
  AND metric_name = 'temperature'
ORDER BY time;

-- 3. Identify any out-of-spec events
SELECT * FROM events
WHERE batch_id = 'B2024-001'
  AND event_type IN ('temperature_excursion', 'specification_deviation')
ORDER BY time;

-- 4. Verify data integrity
SELECT * FROM verify_audit_integrity()
WHERE batch_id = 'B2024-001';
```

## Related Documentation

- [Historical Data Retention Architecture](../../../compliance/HISTORICAL_DATA_RETENTION.md) - Complete technical reference
- [Episode 02: NATS Fundamentals](../02-nats-fundamentals/README.md) - JetStream as hot tier
- [Episode 06: Monitoring](../06-monitoring-observability/README.md) - Real-time visibility into data flow

## Series Complete

This concludes the 7-episode NATS WebSocket Bridge series. For the complete learning path and additional resources, see the [Series Overview](../../SERIES_OVERVIEW.md).
