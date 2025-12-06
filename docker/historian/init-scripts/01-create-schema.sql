-- TimescaleDB Schema for Manufacturing History
-- PharmaCo Historical Data Retention

-- Enable TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================================
-- TELEMETRY HYPERTABLE
-- High-frequency sensor data
-- ============================================================================

CREATE TABLE telemetry (
    time            TIMESTAMPTZ NOT NULL,
    device_id       TEXT NOT NULL,
    line_id         TEXT NOT NULL,
    batch_id        TEXT,
    metric_name     TEXT NOT NULL,
    value           DOUBLE PRECISION NOT NULL,
    unit            TEXT,
    quality         INTEGER DEFAULT 192,  -- OPC UA quality code (192 = Good)

    -- Source tracking
    source_ip       INET,
    correlation_id  UUID,

    -- Integrity
    checksum        TEXT NOT NULL
);

-- Convert to hypertable with 1-day chunks
SELECT create_hypertable('telemetry', 'time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

-- Indexes for common queries
CREATE INDEX idx_telemetry_device ON telemetry (device_id, time DESC);
CREATE INDEX idx_telemetry_batch ON telemetry (batch_id, time DESC) WHERE batch_id IS NOT NULL;
CREATE INDEX idx_telemetry_metric ON telemetry (metric_name, time DESC);
CREATE INDEX idx_telemetry_line ON telemetry (line_id, time DESC);

-- ============================================================================
-- CONTINUOUS AGGREGATES FOR TELEMETRY
-- Pre-computed rollups for faster queries
-- ============================================================================

-- Hourly aggregates
CREATE MATERIALIZED VIEW telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS bucket,
    device_id,
    line_id,
    batch_id,
    metric_name,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    STDDEV(value) as stddev_value,
    COUNT(*) as sample_count,
    SUM(CASE WHEN quality < 192 THEN 1 ELSE 0 END) as bad_quality_count
FROM telemetry
GROUP BY bucket, device_id, line_id, batch_id, metric_name
WITH NO DATA;

-- Daily aggregates
CREATE MATERIALIZED VIEW telemetry_daily
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 day', time) AS bucket,
    device_id,
    line_id,
    batch_id,
    metric_name,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY value) as median_value,
    COUNT(*) as sample_count
FROM telemetry
GROUP BY bucket, device_id, line_id, batch_id, metric_name
WITH NO DATA;

-- Refresh policies for continuous aggregates
SELECT add_continuous_aggregate_policy('telemetry_hourly',
    start_offset => INTERVAL '3 hours',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour'
);

SELECT add_continuous_aggregate_policy('telemetry_daily',
    start_offset => INTERVAL '3 days',
    end_offset => INTERVAL '1 day',
    schedule_interval => INTERVAL '1 day'
);

-- ============================================================================
-- EVENTS HYPERTABLE
-- State changes, alerts, commands
-- ============================================================================

CREATE TABLE events (
    id              UUID DEFAULT gen_random_uuid(),
    time            TIMESTAMPTZ NOT NULL,
    event_type      TEXT NOT NULL,
    severity        TEXT DEFAULT 'info',  -- debug, info, warning, error, critical
    device_id       TEXT NOT NULL,
    line_id         TEXT NOT NULL,
    batch_id        TEXT,
    payload         JSONB NOT NULL,

    -- Causality tracking
    correlation_id  UUID,
    causation_id    UUID,
    sequence_num    BIGINT,

    -- Source tracking
    user_id         TEXT,
    source_ip       INET,

    -- Integrity
    checksum        TEXT NOT NULL,
    previous_hash   TEXT,

    PRIMARY KEY (id, time)
);

SELECT create_hypertable('events', 'time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

-- Indexes
CREATE INDEX idx_events_batch ON events (batch_id, time DESC) WHERE batch_id IS NOT NULL;
CREATE INDEX idx_events_device ON events (device_id, time DESC);
CREATE INDEX idx_events_type ON events (event_type, time DESC);
CREATE INDEX idx_events_severity ON events (severity, time DESC);
CREATE INDEX idx_events_correlation ON events (correlation_id) WHERE correlation_id IS NOT NULL;

-- ============================================================================
-- QUALITY INSPECTIONS
-- Vision scanner results, measurements
-- ============================================================================

CREATE TABLE quality_inspections (
    id              UUID DEFAULT gen_random_uuid(),
    time            TIMESTAMPTZ NOT NULL,
    device_id       TEXT NOT NULL,
    line_id         TEXT NOT NULL,
    batch_id        TEXT NOT NULL,
    product_id      TEXT,

    -- Result
    result          TEXT NOT NULL,  -- pass, fail, review
    defect_type     TEXT,
    defect_details  JSONB,

    -- Measurements
    measurements    JSONB,

    -- Image reference (if stored externally)
    image_ref       TEXT,

    -- Audit
    operator_id     TEXT,
    reviewed_by     TEXT,
    reviewed_at     TIMESTAMPTZ,

    -- Integrity
    checksum        TEXT NOT NULL,

    PRIMARY KEY (id, time)
);

SELECT create_hypertable('quality_inspections', 'time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

CREATE INDEX idx_quality_batch ON quality_inspections (batch_id, time DESC);
CREATE INDEX idx_quality_result ON quality_inspections (result, time DESC);
CREATE INDEX idx_quality_defect ON quality_inspections (defect_type, time DESC) WHERE defect_type IS NOT NULL;

-- ============================================================================
-- BATCH RECORDS
-- Master batch data linking all other records
-- ============================================================================

CREATE TABLE batches (
    batch_id        TEXT PRIMARY KEY,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Batch info
    product_code    TEXT NOT NULL,
    product_name    TEXT NOT NULL,
    lot_number      TEXT NOT NULL,
    line_id         TEXT NOT NULL,

    -- Timing
    scheduled_start TIMESTAMPTZ,
    actual_start    TIMESTAMPTZ,
    actual_end      TIMESTAMPTZ,

    -- Status
    status          TEXT NOT NULL DEFAULT 'planned',  -- planned, in_progress, completed, released, rejected, on_hold

    -- Quantities
    planned_quantity INTEGER,
    actual_quantity  INTEGER,
    good_quantity    INTEGER,
    reject_quantity  INTEGER,

    -- Quality
    oee_availability DECIMAL(5,2),
    oee_performance  DECIMAL(5,2),
    oee_quality      DECIMAL(5,2),
    oee_overall      DECIMAL(5,2),

    -- Deviations
    has_deviations   BOOLEAN DEFAULT FALSE,
    deviation_count  INTEGER DEFAULT 0,

    -- Release
    released_by      TEXT,
    released_at      TIMESTAMPTZ,
    release_notes    TEXT,

    -- Metadata
    metadata         JSONB DEFAULT '{}',

    -- Integrity
    checksum         TEXT NOT NULL,
    version          INTEGER DEFAULT 1
);

CREATE INDEX idx_batches_product ON batches (product_code, created_at DESC);
CREATE INDEX idx_batches_status ON batches (status, created_at DESC);
CREATE INDEX idx_batches_line ON batches (line_id, created_at DESC);

-- ============================================================================
-- AUDIT LOG (IMMUTABLE)
-- Compliance-grade audit trail
-- ============================================================================

CREATE TABLE audit_log (
    id              BIGSERIAL,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Who
    user_id         TEXT,
    user_name       TEXT,
    device_id       TEXT,
    source_ip       INET,

    -- What
    action          TEXT NOT NULL,      -- CREATE, READ, UPDATE, DELETE, LOGIN, LOGOUT, EXPORT, etc.
    resource_type   TEXT NOT NULL,      -- batch, telemetry, event, quality, configuration
    resource_id     TEXT,

    -- Details
    old_value       JSONB,
    new_value       JSONB,
    metadata        JSONB,

    -- Why (required for changes)
    reason          TEXT,

    -- Integrity chain
    checksum        TEXT NOT NULL,
    previous_hash   TEXT NOT NULL,

    PRIMARY KEY (id, timestamp)
);

SELECT create_hypertable('audit_log', 'timestamp',
    chunk_time_interval => INTERVAL '1 month',
    if_not_exists => TRUE
);

-- Prevent any modifications to audit log
CREATE OR REPLACE FUNCTION prevent_audit_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Audit log modifications are not permitted (21 CFR Part 11)';
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_immutable_update
BEFORE UPDATE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_audit_modification();

CREATE TRIGGER audit_immutable_delete
BEFORE DELETE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_audit_modification();

-- Indexes
CREATE INDEX idx_audit_user ON audit_log (user_id, timestamp DESC);
CREATE INDEX idx_audit_resource ON audit_log (resource_type, resource_id, timestamp DESC);
CREATE INDEX idx_audit_action ON audit_log (action, timestamp DESC);

-- ============================================================================
-- RETENTION POLICIES
-- Automated data lifecycle management
-- ============================================================================

-- Raw telemetry: 1 year
SELECT add_retention_policy('telemetry', INTERVAL '1 year');

-- Hourly aggregates: 5 years
SELECT add_retention_policy('telemetry_hourly', INTERVAL '5 years');

-- Daily aggregates: 10 years
SELECT add_retention_policy('telemetry_daily', INTERVAL '10 years');

-- Events: 2 years (then archived to cold storage)
SELECT add_retention_policy('events', INTERVAL '2 years');

-- Quality inspections: 5 years
SELECT add_retention_policy('quality_inspections', INTERVAL '5 years');

-- Audit log: 7 years (regulatory minimum)
SELECT add_retention_policy('audit_log', INTERVAL '7 years');

-- ============================================================================
-- COMPRESSION POLICIES
-- Reduce storage for older data
-- ============================================================================

-- Enable compression on telemetry (after 7 days)
ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, line_id, metric_name'
);
SELECT add_compression_policy('telemetry', INTERVAL '7 days');

-- Enable compression on events (after 30 days)
ALTER TABLE events SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, line_id, event_type'
);
SELECT add_compression_policy('events', INTERVAL '30 days');

-- Enable compression on quality (after 30 days)
ALTER TABLE quality_inspections SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, line_id, batch_id'
);
SELECT add_compression_policy('quality_inspections', INTERVAL '30 days');

-- Enable compression on audit log (after 90 days)
ALTER TABLE audit_log SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'user_id, resource_type'
);
SELECT add_compression_policy('audit_log', INTERVAL '90 days');

-- ============================================================================
-- HELPER FUNCTIONS
-- ============================================================================

-- Calculate checksum for a record
CREATE OR REPLACE FUNCTION calculate_checksum(data JSONB, previous TEXT DEFAULT '')
RETURNS TEXT AS $$
BEGIN
    RETURN encode(
        sha256(
            convert_to(previous || '::' || data::TEXT, 'UTF8')
        ),
        'hex'
    );
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- Get last audit hash (for chain)
CREATE OR REPLACE FUNCTION get_last_audit_hash()
RETURNS TEXT AS $$
DECLARE
    last_hash TEXT;
BEGIN
    SELECT checksum INTO last_hash
    FROM audit_log
    ORDER BY id DESC
    LIMIT 1;

    RETURN COALESCE(last_hash, 'GENESIS');
END;
$$ LANGUAGE plpgsql;

-- Verify audit log integrity
CREATE OR REPLACE FUNCTION verify_audit_integrity(
    start_id BIGINT DEFAULT NULL,
    end_id BIGINT DEFAULT NULL
)
RETURNS TABLE (
    record_id BIGINT,
    status TEXT,
    expected_hash TEXT,
    actual_hash TEXT
) AS $$
BEGIN
    RETURN QUERY
    WITH ordered_logs AS (
        SELECT
            al.id,
            al.checksum,
            al.previous_hash,
            LAG(al.checksum) OVER (ORDER BY al.id) as expected_previous
        FROM audit_log al
        WHERE (start_id IS NULL OR al.id >= start_id)
        AND (end_id IS NULL OR al.id <= end_id)
        ORDER BY al.id
    )
    SELECT
        ol.id as record_id,
        CASE
            WHEN ol.expected_previous IS NULL THEN 'GENESIS'
            WHEN ol.previous_hash = ol.expected_previous THEN 'VALID'
            ELSE 'TAMPERED'
        END as status,
        ol.expected_previous as expected_hash,
        ol.previous_hash as actual_hash
    FROM ordered_logs ol
    WHERE ol.previous_hash != ol.expected_previous
    AND ol.expected_previous IS NOT NULL;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- VIEWS FOR COMMON QUERIES
-- ============================================================================

-- Batch summary view
CREATE VIEW batch_summary AS
SELECT
    b.batch_id,
    b.product_code,
    b.product_name,
    b.lot_number,
    b.line_id,
    b.status,
    b.actual_start,
    b.actual_end,
    b.actual_end - b.actual_start as duration,
    b.actual_quantity,
    b.good_quantity,
    b.reject_quantity,
    ROUND(b.good_quantity::DECIMAL / NULLIF(b.actual_quantity, 0) * 100, 2) as yield_percent,
    b.oee_overall,
    b.has_deviations,
    b.deviation_count,
    (SELECT COUNT(*) FROM events e WHERE e.batch_id = b.batch_id AND e.severity = 'critical') as critical_events,
    (SELECT COUNT(*) FROM quality_inspections q WHERE q.batch_id = b.batch_id AND q.result = 'fail') as failed_inspections
FROM batches b;

-- Device health view
CREATE VIEW device_health AS
SELECT
    device_id,
    line_id,
    MAX(time) as last_seen,
    NOW() - MAX(time) as time_since_last,
    COUNT(*) FILTER (WHERE time > NOW() - INTERVAL '1 hour') as messages_last_hour,
    COUNT(*) FILTER (WHERE time > NOW() - INTERVAL '24 hours') as messages_last_day
FROM events
WHERE time > NOW() - INTERVAL '7 days'
GROUP BY device_id, line_id;

COMMENT ON TABLE telemetry IS 'High-frequency sensor telemetry data with automatic retention and compression';
COMMENT ON TABLE events IS 'State changes, alerts, and commands with causality tracking';
COMMENT ON TABLE quality_inspections IS 'Vision scanner and measurement results';
COMMENT ON TABLE batches IS 'Master batch records linking all manufacturing data';
COMMENT ON TABLE audit_log IS 'Immutable audit trail for 21 CFR Part 11 compliance';
