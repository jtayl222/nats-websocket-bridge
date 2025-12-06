using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Historian.Configuration;
using NatsWebSocketBridge.Historian.Models;
using Npgsql;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// TimescaleDB repository implementation for historian data
/// </summary>
public class TimescaleRepository : IHistorianRepository
{
    private readonly ILogger<TimescaleRepository> _logger;
    private readonly string _connectionString;

    public TimescaleRepository(
        ILogger<TimescaleRepository> logger,
        IOptions<HistorianOptions> options)
    {
        _logger = logger;
        _connectionString = options.Value.ConnectionString;
    }

    #region Telemetry

    public async Task<int> InsertTelemetryBatchAsync(
        IEnumerable<TelemetryRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        const string sql = @"
            INSERT INTO telemetry (time, device_id, line_id, batch_id, metric_name, value, unit, quality, source_ip, correlation_id, checksum)
            VALUES (@Time, @DeviceId, @LineId, @BatchId, @MetricName, @Value, @Unit, @Quality, @SourceIp::inet, @CorrelationId, @Checksum)
            ON CONFLICT DO NOTHING";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var count = 0;
        foreach (var record in recordList)
        {
            try
            {
                count += await connection.ExecuteAsync(sql, new
                {
                    record.Time,
                    record.DeviceId,
                    record.LineId,
                    record.BatchId,
                    record.MetricName,
                    record.Value,
                    record.Unit,
                    record.Quality,
                    SourceIp = record.SourceIp?.ToString(),
                    record.CorrelationId,
                    record.Checksum
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert telemetry record for device {DeviceId}", record.DeviceId);
            }
        }

        _logger.LogDebug("Inserted {Count} telemetry records", count);
        return count;
    }

    public async Task<IEnumerable<TelemetryRecord>> GetTelemetryAsync(
        string deviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? metricName = null,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT time, device_id as DeviceId, line_id as LineId, batch_id as BatchId,
                   metric_name as MetricName, value, unit, quality,
                   source_ip as SourceIp, correlation_id as CorrelationId, checksum
            FROM telemetry
            WHERE device_id = @DeviceId
              AND time >= @Start
              AND time <= @End";

        if (!string.IsNullOrEmpty(metricName))
        {
            sql += " AND metric_name = @MetricName";
        }

        sql += " ORDER BY time DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<TelemetryRecord>(sql, new
        {
            DeviceId = deviceId,
            Start = start,
            End = end,
            MetricName = metricName
        });
    }

    #endregion

    #region Events

    public async Task<int> InsertEventBatchAsync(
        IEnumerable<EventRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        const string sql = @"
            INSERT INTO events (id, time, event_type, severity, device_id, line_id, batch_id, payload,
                              correlation_id, causation_id, sequence_num, user_id, source_ip, checksum, previous_hash)
            VALUES (@Id, @Time, @EventType, @Severity, @DeviceId, @LineId, @BatchId, @Payload::jsonb,
                    @CorrelationId, @CausationId, @SequenceNum, @UserId, @SourceIp::inet, @Checksum, @PreviousHash)
            ON CONFLICT (id, time) DO NOTHING";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var count = 0;
        foreach (var record in recordList)
        {
            try
            {
                count += await connection.ExecuteAsync(sql, new
                {
                    record.Id,
                    record.Time,
                    record.EventType,
                    record.Severity,
                    record.DeviceId,
                    record.LineId,
                    record.BatchId,
                    Payload = record.Payload?.RootElement.GetRawText(),
                    record.CorrelationId,
                    record.CausationId,
                    record.SequenceNum,
                    record.UserId,
                    SourceIp = record.SourceIp?.ToString(),
                    record.Checksum,
                    record.PreviousHash
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert event record {EventId}", record.Id);
            }
        }

        _logger.LogDebug("Inserted {Count} event records", count);
        return count;
    }

    public async Task<IEnumerable<EventRecord>> GetEventsAsync(
        string deviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT id, time, event_type as EventType, severity, device_id as DeviceId,
                   line_id as LineId, batch_id as BatchId, payload::text as PayloadText,
                   correlation_id as CorrelationId, causation_id as CausationId,
                   sequence_num as SequenceNum, user_id as UserId, source_ip as SourceIp,
                   checksum, previous_hash as PreviousHash
            FROM events
            WHERE device_id = @DeviceId
              AND time >= @Start
              AND time <= @End";

        if (!string.IsNullOrEmpty(eventType))
        {
            sql += " AND event_type = @EventType";
        }

        sql += " ORDER BY time DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<dynamic>(sql, new
        {
            DeviceId = deviceId,
            Start = start,
            End = end,
            EventType = eventType
        });

        return results.Select(r => new EventRecord
        {
            Id = r.id,
            Time = r.time,
            EventType = r.eventtype,
            Severity = r.severity,
            DeviceId = r.deviceid,
            LineId = r.lineid,
            BatchId = r.batchid,
            Payload = r.payloadtext != null ? JsonDocument.Parse(r.payloadtext) : null,
            CorrelationId = r.correlationid,
            CausationId = r.causationid,
            SequenceNum = r.sequencenum,
            UserId = r.userid,
            Checksum = r.checksum,
            PreviousHash = r.previoushash
        });
    }

    public async Task<IEnumerable<EventRecord>> GetEventsByBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, time, event_type as EventType, severity, device_id as DeviceId,
                   line_id as LineId, batch_id as BatchId, payload::text as PayloadText,
                   correlation_id as CorrelationId, causation_id as CausationId,
                   sequence_num as SequenceNum, user_id as UserId, checksum, previous_hash as PreviousHash
            FROM events
            WHERE batch_id = @BatchId
            ORDER BY time ASC";

        await using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<dynamic>(sql, new { BatchId = batchId });

        return results.Select(r => new EventRecord
        {
            Id = r.id,
            Time = r.time,
            EventType = r.eventtype,
            Severity = r.severity,
            DeviceId = r.deviceid,
            LineId = r.lineid,
            BatchId = r.batchid,
            Payload = r.payloadtext != null ? JsonDocument.Parse(r.payloadtext) : null,
            CorrelationId = r.correlationid,
            CausationId = r.causationid,
            SequenceNum = r.sequencenum,
            UserId = r.userid,
            Checksum = r.checksum,
            PreviousHash = r.previoushash
        });
    }

    #endregion

    #region Quality Inspections

    public async Task<int> InsertQualityInspectionBatchAsync(
        IEnumerable<QualityInspectionRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        const string sql = @"
            INSERT INTO quality_inspections (id, time, device_id, line_id, batch_id, product_id, result,
                                            defect_type, defect_details, measurements, image_ref, operator_id,
                                            reviewed_by, reviewed_at, checksum)
            VALUES (@Id, @Time, @DeviceId, @LineId, @BatchId, @ProductId, @Result,
                    @DefectType, @DefectDetails::jsonb, @Measurements::jsonb, @ImageRef, @OperatorId,
                    @ReviewedBy, @ReviewedAt, @Checksum)
            ON CONFLICT (id, time) DO NOTHING";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var count = 0;
        foreach (var record in recordList)
        {
            try
            {
                count += await connection.ExecuteAsync(sql, new
                {
                    record.Id,
                    record.Time,
                    record.DeviceId,
                    record.LineId,
                    record.BatchId,
                    record.ProductId,
                    record.Result,
                    record.DefectType,
                    DefectDetails = record.DefectDetails?.RootElement.GetRawText(),
                    Measurements = record.Measurements?.RootElement.GetRawText(),
                    record.ImageRef,
                    record.OperatorId,
                    record.ReviewedBy,
                    record.ReviewedAt,
                    record.Checksum
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert quality inspection {InspectionId}", record.Id);
            }
        }

        _logger.LogDebug("Inserted {Count} quality inspection records", count);
        return count;
    }

    public async Task<IEnumerable<QualityInspectionRecord>> GetQualityInspectionsAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, time, device_id as DeviceId, line_id as LineId, batch_id as BatchId,
                   product_id as ProductId, result, defect_type as DefectType,
                   defect_details::text as DefectDetailsText, measurements::text as MeasurementsText,
                   image_ref as ImageRef, operator_id as OperatorId, reviewed_by as ReviewedBy,
                   reviewed_at as ReviewedAt, checksum
            FROM quality_inspections
            WHERE batch_id = @BatchId
            ORDER BY time ASC";

        await using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<dynamic>(sql, new { BatchId = batchId });

        return results.Select(r => new QualityInspectionRecord
        {
            Id = r.id,
            Time = r.time,
            DeviceId = r.deviceid,
            LineId = r.lineid,
            BatchId = r.batchid,
            ProductId = r.productid,
            Result = r.result,
            DefectType = r.defecttype,
            DefectDetails = r.defectdetailstext != null ? JsonDocument.Parse(r.defectdetailstext) : null,
            Measurements = r.measurementstext != null ? JsonDocument.Parse(r.measurementstext) : null,
            ImageRef = r.imageref,
            OperatorId = r.operatorid,
            ReviewedBy = r.reviewedby,
            ReviewedAt = r.reviewedat,
            Checksum = r.checksum
        });
    }

    #endregion

    #region Batches

    public async Task UpsertBatchAsync(BatchRecord record, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO batches (batch_id, created_at, updated_at, product_code, product_name, lot_number,
                                line_id, scheduled_start, actual_start, actual_end, status,
                                planned_quantity, actual_quantity, good_quantity, reject_quantity,
                                oee_availability, oee_performance, oee_quality, oee_overall,
                                has_deviations, deviation_count, released_by, released_at, release_notes,
                                metadata, checksum, version)
            VALUES (@BatchId, @CreatedAt, @UpdatedAt, @ProductCode, @ProductName, @LotNumber,
                    @LineId, @ScheduledStart, @ActualStart, @ActualEnd, @Status,
                    @PlannedQuantity, @ActualQuantity, @GoodQuantity, @RejectQuantity,
                    @OeeAvailability, @OeePerformance, @OeeQuality, @OeeOverall,
                    @HasDeviations, @DeviationCount, @ReleasedBy, @ReleasedAt, @ReleaseNotes,
                    @Metadata::jsonb, @Checksum, @Version)
            ON CONFLICT (batch_id) DO UPDATE SET
                updated_at = @UpdatedAt,
                actual_start = COALESCE(@ActualStart, batches.actual_start),
                actual_end = COALESCE(@ActualEnd, batches.actual_end),
                status = @Status,
                actual_quantity = COALESCE(@ActualQuantity, batches.actual_quantity),
                good_quantity = COALESCE(@GoodQuantity, batches.good_quantity),
                reject_quantity = COALESCE(@RejectQuantity, batches.reject_quantity),
                oee_availability = COALESCE(@OeeAvailability, batches.oee_availability),
                oee_performance = COALESCE(@OeePerformance, batches.oee_performance),
                oee_quality = COALESCE(@OeeQuality, batches.oee_quality),
                oee_overall = COALESCE(@OeeOverall, batches.oee_overall),
                has_deviations = @HasDeviations,
                deviation_count = @DeviationCount,
                released_by = COALESCE(@ReleasedBy, batches.released_by),
                released_at = COALESCE(@ReleasedAt, batches.released_at),
                release_notes = COALESCE(@ReleaseNotes, batches.release_notes),
                metadata = COALESCE(@Metadata::jsonb, batches.metadata),
                checksum = @Checksum,
                version = batches.version + 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            record.BatchId,
            record.CreatedAt,
            record.UpdatedAt,
            record.ProductCode,
            record.ProductName,
            record.LotNumber,
            record.LineId,
            record.ScheduledStart,
            record.ActualStart,
            record.ActualEnd,
            record.Status,
            record.PlannedQuantity,
            record.ActualQuantity,
            record.GoodQuantity,
            record.RejectQuantity,
            record.OeeAvailability,
            record.OeePerformance,
            record.OeeQuality,
            record.OeeOverall,
            record.HasDeviations,
            record.DeviationCount,
            record.ReleasedBy,
            record.ReleasedAt,
            record.ReleaseNotes,
            Metadata = record.Metadata?.RootElement.GetRawText(),
            record.Checksum,
            record.Version
        });

        _logger.LogDebug("Upserted batch {BatchId}", record.BatchId);
    }

    public async Task<BatchRecord?> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT batch_id as BatchId, created_at as CreatedAt, updated_at as UpdatedAt,
                   product_code as ProductCode, product_name as ProductName, lot_number as LotNumber,
                   line_id as LineId, scheduled_start as ScheduledStart, actual_start as ActualStart,
                   actual_end as ActualEnd, status, planned_quantity as PlannedQuantity,
                   actual_quantity as ActualQuantity, good_quantity as GoodQuantity,
                   reject_quantity as RejectQuantity, oee_availability as OeeAvailability,
                   oee_performance as OeePerformance, oee_quality as OeeQuality,
                   oee_overall as OeeOverall, has_deviations as HasDeviations,
                   deviation_count as DeviationCount, released_by as ReleasedBy,
                   released_at as ReleasedAt, release_notes as ReleaseNotes,
                   metadata::text as MetadataText, checksum, version
            FROM batches
            WHERE batch_id = @BatchId";

        await using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { BatchId = batchId });

        if (result == null) return null;

        return new BatchRecord
        {
            BatchId = result.batchid,
            CreatedAt = result.createdat,
            UpdatedAt = result.updatedat,
            ProductCode = result.productcode,
            ProductName = result.productname,
            LotNumber = result.lotnumber,
            LineId = result.lineid,
            ScheduledStart = result.scheduledstart,
            ActualStart = result.actualstart,
            ActualEnd = result.actualend,
            Status = result.status,
            PlannedQuantity = result.plannedquantity,
            ActualQuantity = result.actualquantity,
            GoodQuantity = result.goodquantity,
            RejectQuantity = result.rejectquantity,
            OeeAvailability = result.oeeavailability,
            OeePerformance = result.oeeperformance,
            OeeQuality = result.oeequality,
            OeeOverall = result.oeeoverall,
            HasDeviations = result.hasdeviations,
            DeviationCount = result.deviationcount,
            ReleasedBy = result.releasedby,
            ReleasedAt = result.releasedat,
            ReleaseNotes = result.releasenotes,
            Metadata = result.metadatatext != null ? JsonDocument.Parse(result.metadatatext) : null,
            Checksum = result.checksum,
            Version = result.version
        };
    }

    public async Task<IEnumerable<BatchRecord>> GetBatchesByStatusAsync(
        string status,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT batch_id as BatchId, created_at as CreatedAt, updated_at as UpdatedAt,
                   product_code as ProductCode, product_name as ProductName, lot_number as LotNumber,
                   line_id as LineId, status, actual_start as ActualStart, actual_end as ActualEnd,
                   actual_quantity as ActualQuantity, good_quantity as GoodQuantity,
                   oee_overall as OeeOverall, has_deviations as HasDeviations, checksum
            FROM batches
            WHERE status = @Status
            ORDER BY created_at DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<BatchRecord>(sql, new { Status = status });
    }

    #endregion

    #region Audit Log

    public async Task InsertAuditLogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO audit_log (timestamp, user_id, user_name, device_id, source_ip, action,
                                  resource_type, resource_id, old_value, new_value, metadata,
                                  reason, checksum, previous_hash)
            VALUES (@Timestamp, @UserId, @UserName, @DeviceId, @SourceIp::inet, @Action,
                    @ResourceType, @ResourceId, @OldValue::jsonb, @NewValue::jsonb, @Metadata::jsonb,
                    @Reason, @Checksum, @PreviousHash)";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            entry.Timestamp,
            entry.UserId,
            entry.UserName,
            entry.DeviceId,
            SourceIp = entry.SourceIp?.ToString(),
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            OldValue = entry.OldValue?.RootElement.GetRawText(),
            NewValue = entry.NewValue?.RootElement.GetRawText(),
            Metadata = entry.Metadata?.RootElement.GetRawText(),
            entry.Reason,
            entry.Checksum,
            entry.PreviousHash
        });

        _logger.LogDebug("Inserted audit log entry: {Action} on {ResourceType}/{ResourceId}",
            entry.Action, entry.ResourceType, entry.ResourceId);
    }

    public async Task<string> GetLastAuditHashAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT checksum FROM audit_log ORDER BY id DESC LIMIT 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
        return result ?? "GENESIS";
    }

    public async Task<IEnumerable<AuditLogEntry>> GetAuditLogAsync(
        string resourceType,
        string? resourceId = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT id, timestamp, user_id as UserId, user_name as UserName, device_id as DeviceId,
                   source_ip as SourceIp, action, resource_type as ResourceType, resource_id as ResourceId,
                   old_value::text as OldValueText, new_value::text as NewValueText,
                   metadata::text as MetadataText, reason, checksum, previous_hash as PreviousHash
            FROM audit_log
            WHERE resource_type = @ResourceType";

        if (!string.IsNullOrEmpty(resourceId))
        {
            sql += " AND resource_id = @ResourceId";
        }
        if (start.HasValue)
        {
            sql += " AND timestamp >= @Start";
        }
        if (end.HasValue)
        {
            sql += " AND timestamp <= @End";
        }

        sql += " ORDER BY id ASC";

        await using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<dynamic>(sql, new
        {
            ResourceType = resourceType,
            ResourceId = resourceId,
            Start = start,
            End = end
        });

        return results.Select(r => new AuditLogEntry
        {
            Id = r.id,
            Timestamp = r.timestamp,
            UserId = r.userid,
            UserName = r.username,
            DeviceId = r.deviceid,
            Action = r.action,
            ResourceType = r.resourcetype,
            ResourceId = r.resourceid,
            OldValue = r.oldvaluetext != null ? JsonDocument.Parse(r.oldvaluetext) : null,
            NewValue = r.newvaluetext != null ? JsonDocument.Parse(r.newvaluetext) : null,
            Metadata = r.metadatatext != null ? JsonDocument.Parse(r.metadatatext) : null,
            Reason = r.reason,
            Checksum = r.checksum,
            PreviousHash = r.previoushash
        });
    }

    #endregion
}
