# Episode 07: Historical Data Retention - Demo Script

## Setup

```bash
# Start the historian stack
cd docker/historian
docker-compose up -d

# Verify all services
docker-compose ps

# Expected:
# nats         Up    0.0.0.0:4222->4222/tcp
# timescaledb  Up    0.0.0.0:5432->5432/tcp
# historian    Up
# minio        Up    0.0.0.0:9000->9000/tcp
```

---

## Demo 1: Connect to TimescaleDB

```bash
# Connect via psql
psql -h localhost -U historian -d manufacturing_history

# Or via docker
docker exec -it historian-timescaledb psql -U historian manufacturing_history

# Check TimescaleDB version
SELECT extversion FROM pg_extension WHERE extname = 'timescaledb';
```

---

## Demo 2: Explore the Schema

```sql
-- List all tables
\dt

-- Show hypertables
SELECT * FROM timescaledb_information.hypertables;

-- Describe telemetry table
\d telemetry

-- Show indexes
\di
```

---

## Demo 3: Create Hypertable

```sql
-- If starting fresh, create the telemetry table
CREATE TABLE IF NOT EXISTS telemetry (
    time         TIMESTAMPTZ NOT NULL,
    device_id    TEXT NOT NULL,
    metric_name  TEXT NOT NULL,
    value        DOUBLE PRECISION NOT NULL,
    unit         TEXT,
    quality      TEXT DEFAULT 'good',
    checksum     TEXT NOT NULL
);

-- Convert to hypertable
SELECT create_hypertable('telemetry', 'time', if_not_exists => TRUE);

-- View the chunks
SELECT show_chunks('telemetry');
```

---

## Demo 4: Insert Sample Data

```sql
-- Insert telemetry data
INSERT INTO telemetry (time, device_id, metric_name, value, unit, checksum)
VALUES
    (NOW() - INTERVAL '1 hour', 'SENSOR-001', 'temperature', 23.5, 'C', 'abc123'),
    (NOW() - INTERVAL '55 minutes', 'SENSOR-001', 'temperature', 23.7, 'C', 'def456'),
    (NOW() - INTERVAL '50 minutes', 'SENSOR-001', 'temperature', 23.4, 'C', 'ghi789'),
    (NOW() - INTERVAL '45 minutes', 'SENSOR-001', 'temperature', 24.1, 'C', 'jkl012');

-- Generate more data (100 points per sensor)
INSERT INTO telemetry (time, device_id, metric_name, value, unit, checksum)
SELECT
    NOW() - (n || ' minutes')::INTERVAL,
    'SENSOR-00' || (1 + (n % 5)),
    'temperature',
    20 + random() * 10,
    'C',
    md5(random()::text)
FROM generate_series(1, 500) AS n;

-- Verify data
SELECT COUNT(*) FROM telemetry;
SELECT device_id, COUNT(*) FROM telemetry GROUP BY device_id;
```

---

## Demo 5: Time-Series Queries

```sql
-- Last hour of data for one sensor
SELECT time, value
FROM telemetry
WHERE device_id = 'SENSOR-001'
  AND time > NOW() - INTERVAL '1 hour'
ORDER BY time;

-- Average by 10-minute buckets
SELECT
    time_bucket('10 minutes', time) AS bucket,
    device_id,
    AVG(value) as avg_temp,
    MIN(value) as min_temp,
    MAX(value) as max_temp
FROM telemetry
WHERE time > NOW() - INTERVAL '1 hour'
GROUP BY bucket, device_id
ORDER BY bucket, device_id;
```

---

## Demo 6: Create Continuous Aggregate

```sql
-- Create hourly aggregate view
CREATE MATERIALIZED VIEW telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS bucket,
    device_id,
    metric_name,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    COUNT(*) as sample_count
FROM telemetry
GROUP BY bucket, device_id, metric_name;

-- Add refresh policy (every 30 minutes, refresh last 2 hours)
SELECT add_continuous_aggregate_policy('telemetry_hourly',
    start_offset => INTERVAL '2 hours',
    end_offset => INTERVAL '1 minute',
    schedule_interval => INTERVAL '30 minutes');

-- Query the aggregate
SELECT * FROM telemetry_hourly
WHERE bucket > NOW() - INTERVAL '24 hours'
ORDER BY bucket DESC;
```

---

## Demo 7: Compare Query Performance

```sql
-- Insert lots of data for comparison
INSERT INTO telemetry (time, device_id, metric_name, value, unit, checksum)
SELECT
    NOW() - (n || ' seconds')::INTERVAL,
    'SENSOR-00' || (1 + (n % 10)),
    'temperature',
    20 + random() * 10,
    'C',
    md5(random()::text)
FROM generate_series(1, 100000) AS n;

-- Raw query (explain analyze)
EXPLAIN ANALYZE
SELECT
    time_bucket('1 hour', time) AS bucket,
    device_id,
    AVG(value)
FROM telemetry
WHERE device_id = 'SENSOR-001'
  AND time > NOW() - INTERVAL '24 hours'
GROUP BY bucket, device_id;

-- Aggregate query (explain analyze)
EXPLAIN ANALYZE
SELECT *
FROM telemetry_hourly
WHERE device_id = 'SENSOR-001'
  AND bucket > NOW() - INTERVAL '24 hours';
```

---

## Demo 8: Enable Compression

```sql
-- Enable compression on telemetry
ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, metric_name',
    timescaledb.compress_orderby = 'time DESC'
);

-- Add compression policy (compress data older than 7 days)
SELECT add_compression_policy('telemetry', INTERVAL '7 days');

-- Manually compress for demo
SELECT compress_chunk(c) FROM show_chunks('telemetry') c;

-- Check compression stats
SELECT
    chunk_name,
    pg_size_pretty(before_compression_total_bytes) as before,
    pg_size_pretty(after_compression_total_bytes) as after,
    round((1 - after_compression_total_bytes::numeric / before_compression_total_bytes) * 100, 2) as reduction_pct
FROM chunk_compression_stats('telemetry');
```

---

## Demo 9: Retention Policy

```sql
-- Add retention policy (keep 30 days for demo)
SELECT add_retention_policy('telemetry', INTERVAL '30 days');

-- View all policies
SELECT * FROM timescaledb_information.jobs
WHERE proc_name IN ('policy_compression', 'policy_retention');

-- Manually run retention (for demo)
CALL run_job((SELECT job_id FROM timescaledb_information.jobs WHERE proc_name = 'policy_retention'));
```

---

## Demo 10: Audit Log

```sql
-- Create audit log table
CREATE TABLE audit_log (
    id              BIGSERIAL PRIMARY KEY,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    action          TEXT NOT NULL,
    resource_type   TEXT NOT NULL,
    resource_id     TEXT,
    old_value       JSONB,
    new_value       JSONB,
    user_id         TEXT,
    reason          TEXT,
    checksum        TEXT NOT NULL,
    previous_hash   TEXT
);

-- Make it immutable
CREATE OR REPLACE FUNCTION prevent_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Audit log modifications are not allowed';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_immutable_update
BEFORE UPDATE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_modification();

CREATE TRIGGER audit_immutable_delete
BEFORE DELETE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_modification();
```

---

## Demo 11: Insert Audit Entries

```sql
-- First entry (no previous hash)
INSERT INTO audit_log (action, resource_type, resource_id, new_value, user_id, reason, checksum)
VALUES (
    'CREATE',
    'batch_record',
    'BATCH-2024-001',
    '{"product": "Widget-A", "quantity": 1000}'::jsonb,
    'operator-01',
    'New production batch started',
    md5('CREATE|batch_record|BATCH-2024-001')
);

-- Second entry (chained)
INSERT INTO audit_log (action, resource_type, resource_id, new_value, user_id, reason, checksum, previous_hash)
VALUES (
    'UPDATE',
    'batch_record',
    'BATCH-2024-001',
    '{"product": "Widget-A", "quantity": 1000, "status": "in_progress"}'::jsonb,
    'operator-01',
    'Batch started processing',
    md5('UPDATE|batch_record|BATCH-2024-001|' || (SELECT checksum FROM audit_log ORDER BY id DESC LIMIT 1)),
    (SELECT checksum FROM audit_log ORDER BY id DESC LIMIT 1)
);

-- View audit trail
SELECT id, timestamp, action, resource_type, resource_id, user_id, reason
FROM audit_log
ORDER BY id;
```

---

## Demo 12: Test Immutability

```sql
-- Try to update (should fail)
UPDATE audit_log SET reason = 'Modified reason' WHERE id = 1;
-- ERROR: Audit log modifications are not allowed

-- Try to delete (should fail)
DELETE FROM audit_log WHERE id = 1;
-- ERROR: Audit log modifications are not allowed
```

---

## Demo 13: Verify Integrity

```sql
-- Create verification function
CREATE OR REPLACE FUNCTION verify_audit_integrity()
RETURNS TABLE (
    id BIGINT,
    is_valid BOOLEAN,
    expected_previous TEXT,
    actual_previous TEXT
) AS $$
DECLARE
    prev_hash TEXT := NULL;
    entry RECORD;
BEGIN
    FOR entry IN SELECT * FROM audit_log ORDER BY id
    LOOP
        id := entry.id;
        expected_previous := prev_hash;
        actual_previous := entry.previous_hash;
        is_valid := (prev_hash IS NULL AND entry.previous_hash IS NULL)
                    OR (prev_hash = entry.previous_hash);
        prev_hash := entry.checksum;
        RETURN NEXT;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

-- Run verification
SELECT * FROM verify_audit_integrity();
```

---

## Demo 14: Start Historian Service

```bash
# Terminal 2: Start the gateway (generates data)
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Terminal 3: Start the historian
cd src/NatsWebSocketBridge.Historian
dotnet run

# Watch historian logs
# [INF] Connected to NATS
# [INF] Starting JetStream consumers
# [INF] Consuming from TELEMETRY stream
# [INF] Batch insert: 100 telemetry records

# Terminal 4: Generate traffic
wscat -c ws://localhost:5000/ws -x '{"type":"AUTH","deviceId":"demo","credentials":{"apiKey":"test"}}'
# Publish messages...
```

---

## Demo 15: Query Historical Data via API

```bash
# Query telemetry history
curl "http://localhost:5001/api/history/telemetry?device_id=SENSOR-001&start=2024-01-01&end=2024-12-31" | jq

# Query with aggregation
curl "http://localhost:5001/api/history/telemetry?device_id=SENSOR-001&aggregate=hourly" | jq

# Query audit log
curl "http://localhost:5001/api/audit?resource_type=batch_record" | jq

# Verify integrity via API
curl "http://localhost:5001/api/audit/verify" | jq
```

---

## Demo 16: MinIO Cold Storage

```bash
# Access MinIO console
open http://localhost:9000
# Login: minioadmin / minioadmin

# Or via CLI
docker exec -it historian-minio mc ls local/archives/

# Trigger archival manually (if configured)
curl -X POST http://localhost:5001/api/admin/archive
```

---

## Cleanup

```bash
# Stop all services
cd docker/historian
docker-compose down -v

# Remove volumes (optional)
docker volume prune
```

---

## Troubleshooting

### TimescaleDB connection refused
```bash
# Check container is running
docker ps | grep timescaledb

# Check logs
docker logs historian-timescaledb

# Test connection
pg_isready -h localhost -p 5432
```

### Historian not receiving messages
```bash
# Verify NATS JetStream streams exist
nats stream ls

# Check consumer status
nats consumer info TELEMETRY historian

# Check historian logs for errors
docker logs historian
```

### Compression not working
```sql
-- Check if compression is enabled
SELECT * FROM timescaledb_information.compression_settings;

-- Check chunk status
SELECT chunk_name, is_compressed
FROM timescaledb_information.chunks
WHERE hypertable_name = 'telemetry';
```
