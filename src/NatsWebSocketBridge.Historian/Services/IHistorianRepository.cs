using NatsWebSocketBridge.Historian.Models;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// Repository interface for TimescaleDB historian operations
/// </summary>
public interface IHistorianRepository
{
    // Telemetry operations
    Task<int> InsertTelemetryBatchAsync(IEnumerable<TelemetryRecord> records, CancellationToken cancellationToken = default);
    Task<IEnumerable<TelemetryRecord>> GetTelemetryAsync(
        string deviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? metricName = null,
        CancellationToken cancellationToken = default);

    // Event operations
    Task<int> InsertEventBatchAsync(IEnumerable<EventRecord> records, CancellationToken cancellationToken = default);
    Task<IEnumerable<EventRecord>> GetEventsAsync(
        string deviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? eventType = null,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<EventRecord>> GetEventsByBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    // Quality inspection operations
    Task<int> InsertQualityInspectionBatchAsync(IEnumerable<QualityInspectionRecord> records, CancellationToken cancellationToken = default);
    Task<IEnumerable<QualityInspectionRecord>> GetQualityInspectionsAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    // Batch operations
    Task UpsertBatchAsync(BatchRecord record, CancellationToken cancellationToken = default);
    Task<BatchRecord?> GetBatchAsync(string batchId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BatchRecord>> GetBatchesByStatusAsync(string status, CancellationToken cancellationToken = default);

    // Audit log operations
    Task InsertAuditLogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<string> GetLastAuditHashAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLogEntry>> GetAuditLogAsync(
        string resourceType,
        string? resourceId = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default);
}
