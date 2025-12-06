# Episode 07: Historical Data Retention - Slides

---

## Slide 1: Title

# Historical Data Retention
## Compliance-Ready Time-Series Storage

**NATS WebSocket Bridge Series - Episode 07**

---

## Slide 2: Episode Goals

### What You'll Learn

- FDA 21 CFR Part 11 requirements
- Three-tier retention architecture
- TimescaleDB for time-series data
- Tamper-evident audit trails

---

## Slide 3: The Compliance Challenge

### "What happened 3 weeks ago?"

**Regulatory Requirements:**
- FDA 21 CFR Part 11 (Pharmaceuticals)
- GxP / GAMP 5 guidelines
- SOX compliance (Financial)
- ISO 22000 (Food safety)

**Common Questions:**
- Can you prove this batch was made correctly?
- When did the temperature exceed limits?
- Who changed the setpoint?
- Is the data tamper-proof?

---

## Slide 4: ALCOA+ Principles

### Data Integrity Standards

| Principle | Meaning | Implementation |
|-----------|---------|----------------|
| **A**ttributable | Who did it? | device_id, user_id |
| **L**egible | Can we read it? | Structured data |
| **C**ontemporaneous | Recorded when it happened | Timestamps |
| **O**riginal | First capture | Checksums |
| **A**ccurate | True and complete | Validation |
| **+Complete** | All data present | No gaps |
| **+Consistent** | Same across systems | Hash chains |
| **+Enduring** | Available long-term | Tiered storage |
| **+Available** | Accessible when needed | Query APIs |

---

## Slide 5: Three-Tier Storage Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Data Flow                             │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│  HOT TIER: JetStream (7 days)                           │
│  ├─ Real-time queries                                   │
│  ├─ Active subscriptions                                │
│  └─ Message replay                                      │
└────────────────────────┬────────────────────────────────┘
                         │ Historian Service
                         ▼
┌─────────────────────────────────────────────────────────┐
│  WARM TIER: TimescaleDB (1 year)                        │
│  ├─ Time-series queries                                 │
│  ├─ Continuous aggregates                               │
│  └─ Compression enabled                                 │
└────────────────────────┬────────────────────────────────┘
                         │ Archival Service
                         ▼
┌─────────────────────────────────────────────────────────┐
│  COLD TIER: S3/MinIO (10+ years)                        │
│  ├─ Compressed archives                                 │
│  ├─ Immutable storage                                   │
│  └─ Glacier-compatible                                  │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 6: Why TimescaleDB?

### Time-Series Optimized PostgreSQL

| Feature | PostgreSQL | TimescaleDB |
|---------|------------|-------------|
| Time-based partitioning | Manual | Automatic (hypertables) |
| Data compression | None | 10-100x compression |
| Continuous aggregates | Manual views | Auto-updating |
| Retention policies | Manual DELETE | Declarative |
| Query performance | Degrades | Consistent |

**It's still PostgreSQL:**
- SQL compatibility
- Existing tools work
- ACID transactions
- Rich ecosystem

---

## Slide 7: Hypertables

### Automatic Time Partitioning

```sql
-- Create a regular table
CREATE TABLE telemetry (
    time        TIMESTAMPTZ NOT NULL,
    device_id   TEXT NOT NULL,
    metric_name TEXT NOT NULL,
    value       DOUBLE PRECISION NOT NULL,
    checksum    TEXT NOT NULL
);

-- Convert to hypertable (magic happens here)
SELECT create_hypertable('telemetry', 'time');

-- Now inserts are automatically partitioned by time
INSERT INTO telemetry VALUES (NOW(), 'SENSOR-001', 'temp', 23.5, 'abc123');
```

**Behind the scenes:**
```
telemetry (hypertable)
├── _hyper_1_1_chunk  (2024-01-01 to 2024-01-08)
├── _hyper_1_2_chunk  (2024-01-08 to 2024-01-15)
└── _hyper_1_3_chunk  (2024-01-15 to 2024-01-22)
```

---

## Slide 8: Complete Schema

```sql
-- Telemetry hypertable
CREATE TABLE telemetry (
    time         TIMESTAMPTZ NOT NULL,
    device_id    TEXT NOT NULL,
    metric_name  TEXT NOT NULL,
    value        DOUBLE PRECISION NOT NULL,
    unit         TEXT,
    quality      TEXT DEFAULT 'good',
    checksum     TEXT NOT NULL
);
SELECT create_hypertable('telemetry', 'time');

-- Events table
CREATE TABLE events (
    time         TIMESTAMPTZ NOT NULL,
    device_id    TEXT NOT NULL,
    event_type   TEXT NOT NULL,
    severity     TEXT NOT NULL,
    message      TEXT,
    metadata     JSONB,
    checksum     TEXT NOT NULL
);
SELECT create_hypertable('events', 'time');

-- Batch records (for manufacturing)
CREATE TABLE batch_records (
    batch_id     TEXT PRIMARY KEY,
    product_code TEXT NOT NULL,
    started_at   TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ,
    status       TEXT NOT NULL,
    parameters   JSONB,
    checksum     TEXT NOT NULL
);
```

---

## Slide 9: Historian Service Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  Historian Service                       │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  JetStream  │  │   Channel   │  │  Batch      │     │
│  │  Consumer   │→ │   Buffer    │→ │  Writer     │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│         │                                   │           │
│         ▼                                   ▼           │
│  ┌─────────────┐                   ┌─────────────┐     │
│  │  Checksum   │                   │ TimescaleDB │     │
│  │  Service    │                   │ Repository  │     │
│  └─────────────┘                   └─────────────┘     │
│                                            │            │
│  ┌─────────────┐                           │            │
│  │  Audit Log  │◄──────────────────────────┘            │
│  │  Service    │                                        │
│  └─────────────┘                                        │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 10: Checksum Computation

### Ensuring Data Integrity

```csharp
public class ChecksumService : IChecksumService
{
    public string ComputeChecksum(object data, string? previousHash = null)
    {
        var json = JsonSerializer.Serialize(data);
        var input = previousHash != null
            ? previousHash + json
            : json;

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    // Chain checksums for tamper detection
    public string ComputeChainedChecksum(object data, string previousHash)
    {
        return ComputeChecksum(data, previousHash);
    }
}
```

**Why chain checksums?**
- Any modification breaks the chain
- Can verify entire history
- Similar to blockchain concept

---

## Slide 11: Continuous Aggregates

### Pre-Computed Rollups

```sql
-- Create hourly aggregate (auto-updates)
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

-- Query is instant, no matter how much data
SELECT * FROM telemetry_hourly
WHERE device_id = 'SENSOR-001'
  AND bucket > NOW() - INTERVAL '24 hours';
```

**Performance benefit:**
- Raw query: 10 million rows, 30 seconds
- Aggregate query: 240 rows, 5 milliseconds

---

## Slide 12: Compression Policies

### Automatic Data Compression

```sql
-- Enable compression on hypertable
ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id,metric_name',
    timescaledb.compress_orderby = 'time DESC'
);

-- Add compression policy (compress data older than 7 days)
SELECT add_compression_policy('telemetry', INTERVAL '7 days');

-- Check compression stats
SELECT
    chunk_name,
    before_compression_total_bytes,
    after_compression_total_bytes,
    (1 - after_compression_total_bytes::float / before_compression_total_bytes) * 100 as reduction_pct
FROM chunk_compression_stats('telemetry');
```

**Typical results:**
- Sensor data: 90-95% reduction
- 100GB raw → 5-10GB compressed

---

## Slide 13: Retention Policies

### Automatic Data Lifecycle

```sql
-- Drop data older than 1 year
SELECT add_retention_policy('telemetry', INTERVAL '1 year');

-- View active policies
SELECT * FROM timescaledb_information.jobs
WHERE proc_name = 'policy_retention';

-- Different retention per table
SELECT add_retention_policy('events', INTERVAL '90 days');
SELECT add_retention_policy('batch_records', INTERVAL '7 years');
```

**Before dropping, archive to cold storage!**

---

## Slide 14: Audit Log Design

### Immutable Record of All Changes

```sql
CREATE TABLE audit_log (
    id              BIGSERIAL PRIMARY KEY,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    action          TEXT NOT NULL,          -- CREATE, READ, UPDATE, DELETE
    resource_type   TEXT NOT NULL,          -- telemetry, batch, config
    resource_id     TEXT,
    old_value       JSONB,
    new_value       JSONB,
    user_id         TEXT,
    reason          TEXT,                   -- Why was this done?
    checksum        TEXT NOT NULL,          -- Hash chain
    previous_hash   TEXT                    -- Links to previous entry
);

-- Prevent modifications
CREATE OR REPLACE FUNCTION prevent_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Audit log is immutable';
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

## Slide 15: Hash Chain Verification

```
┌─────────────────────────────────────────────────────────┐
│                    Audit Hash Chain                      │
└─────────────────────────────────────────────────────────┘

┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  Entry #1   │    │  Entry #2   │    │  Entry #3   │
│             │    │             │    │             │
│ data: {...} │    │ data: {...} │    │ data: {...} │
│ hash: ABC   │───→│ prev: ABC   │───→│ prev: DEF   │
│             │    │ hash: DEF   │    │ hash: GHI   │
└─────────────┘    └─────────────┘    └─────────────┘

If Entry #2 is modified:
                   ┌─────────────┐
                   │  Entry #2   │
                   │  MODIFIED   │
                   │ hash: XYZ   │  ← Hash changes!
                   └─────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│  Entry #3 expects prev: DEF, but #2 now has hash: XYZ   │
│  CHAIN BROKEN - TAMPERING DETECTED!                     │
└─────────────────────────────────────────────────────────┘
```

---

## Slide 16: Integrity Verification

```csharp
public async Task<AuditIntegrityResult> VerifyIntegrityAsync(
    long? startId = null, long? endId = null)
{
    var entries = await _repository.GetAuditEntriesAsync(startId, endId);

    string? previousHash = null;
    var brokenLinks = new List<long>();

    foreach (var entry in entries)
    {
        // Verify chain link
        if (previousHash != null && entry.PreviousHash != previousHash)
        {
            brokenLinks.Add(entry.Id);
        }

        // Recompute and verify checksum
        var expectedChecksum = _checksumService.ComputeChecksum(
            new { entry.Action, entry.ResourceType, entry.NewValue },
            entry.PreviousHash);

        if (entry.Checksum != expectedChecksum)
        {
            brokenLinks.Add(entry.Id);
        }

        previousHash = entry.Checksum;
    }

    return new AuditIntegrityResult
    {
        IsValid = brokenLinks.Count == 0,
        EntriesChecked = entries.Count,
        BrokenLinks = brokenLinks
    };
}
```

---

## Slide 17: Cold Storage Archival

### S3/MinIO for Long-Term Storage

```csharp
public class ArchivalService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Find data ready for archival
            var oldData = await _repository.GetDataOlderThanAsync(
                _options.ArchiveThreshold);

            foreach (var batch in oldData.Chunk(1000))
            {
                // Export as compressed JSON
                var archive = CreateArchive(batch);

                // Upload to S3/MinIO
                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _options.Bucket,
                    Key = $"archives/{DateTime.UtcNow:yyyy/MM}/batch_{Guid.NewGuid()}.json.gz",
                    InputStream = archive,
                    ContentType = "application/gzip"
                });

                // Log archival in audit trail
                await _auditLog.LogExportAsync(
                    "telemetry_archive",
                    batch.First().Time.ToString(),
                    reason: "Scheduled archival");
            }

            await Task.Delay(_options.ArchiveInterval, ct);
        }
    }
}
```

---

## Slide 18: Query API

### Historical Data Access

```csharp
// Time range queries
GET /api/history/telemetry
    ?device_id=SENSOR-001
    &start=2024-01-01T00:00:00Z
    &end=2024-01-31T23:59:59Z
    &aggregate=hourly

// Response
{
    "device_id": "SENSOR-001",
    "period": { "start": "...", "end": "..." },
    "aggregation": "hourly",
    "data": [
        { "time": "2024-01-01T00:00:00Z", "avg": 23.2, "min": 22.1, "max": 24.5 },
        { "time": "2024-01-01T01:00:00Z", "avg": 23.4, "min": 22.3, "max": 24.8 },
        ...
    ]
}

// Batch record lookup
GET /api/history/batches/BATCH-2024-001

// Audit trail query
GET /api/audit
    ?resource_type=batch_records
    &resource_id=BATCH-2024-001
    &action=UPDATE
```

---

## Slide 19: Compliance Checklist

### FDA 21 CFR Part 11 Readiness

| Requirement | Implementation |
|-------------|----------------|
| Electronic signatures | User ID + reason for change |
| Audit trail | Immutable audit_log table |
| Record retention | Tiered storage with policies |
| Data integrity | SHA-256 checksums |
| Access controls | Role-based permissions |
| Backup/recovery | PostgreSQL + S3 replication |
| Validation | Integrity verification API |

**Documentation required:**
- System validation protocols
- Change control procedures
- Backup/recovery procedures
- User training records

---

## Slide 20: Series Wrap-Up

# Thank You!

### What We Covered

1. NATS fundamentals and JetStream
2. Gateway architecture and middleware
3. WebSocket protocol design
4. C++ Device SDK
5. Monitoring with PLG stack
6. Historical data retention

**Resources:**
- GitHub: github.com/company/nats-websocket-bridge
- Documentation: docs.company.com/nats-bridge
- Community: discord.gg/nats-bridge

**Questions?**
