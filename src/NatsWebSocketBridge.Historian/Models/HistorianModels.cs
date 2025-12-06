using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NatsWebSocketBridge.Historian.Models;

/// <summary>
/// Telemetry record for TimescaleDB
/// </summary>
public class TelemetryRecord
{
    public DateTimeOffset Time { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string? BatchId { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Unit { get; set; }
    public int Quality { get; set; } = 192; // OPC UA Good
    public IPAddress? SourceIp { get; set; }
    public Guid? CorrelationId { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>
/// Event record for TimescaleDB
/// </summary>
public class EventRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string DeviceId { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string? BatchId { get; set; }
    public JsonDocument? Payload { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public long? SequenceNum { get; set; }
    public string? UserId { get; set; }
    public IPAddress? SourceIp { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? PreviousHash { get; set; }
}

/// <summary>
/// Quality inspection record for TimescaleDB
/// </summary>
public class QualityInspectionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string? ProductId { get; set; }
    public string Result { get; set; } = "pass"; // pass, fail, review
    public string? DefectType { get; set; }
    public JsonDocument? DefectDetails { get; set; }
    public JsonDocument? Measurements { get; set; }
    public string? ImageRef { get; set; }
    public string? OperatorId { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>
/// Batch record for TimescaleDB
/// </summary>
public class BatchRecord
{
    public string BatchId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public DateTimeOffset? ScheduledStart { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public string Status { get; set; } = "planned";
    public int? PlannedQuantity { get; set; }
    public int? ActualQuantity { get; set; }
    public int? GoodQuantity { get; set; }
    public int? RejectQuantity { get; set; }
    public decimal? OeeAvailability { get; set; }
    public decimal? OeePerformance { get; set; }
    public decimal? OeeQuality { get; set; }
    public decimal? OeeOverall { get; set; }
    public bool HasDeviations { get; set; }
    public int DeviationCount { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public JsonDocument? Metadata { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}

/// <summary>
/// Audit log entry for TimescaleDB (immutable)
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? DeviceId { get; set; }
    public IPAddress? SourceIp { get; set; }
    public string Action { get; set; } = string.Empty; // CREATE, READ, UPDATE, DELETE, etc.
    public string ResourceType { get; set; } = string.Empty; // batch, telemetry, event, etc.
    public string? ResourceId { get; set; }
    public JsonDocument? OldValue { get; set; }
    public JsonDocument? NewValue { get; set; }
    public JsonDocument? Metadata { get; set; }
    public string? Reason { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
}

/// <summary>
/// Incoming message from JetStream (generic container)
/// </summary>
public class IncomingMessage
{
    public string Subject { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Dictionary<string, string> Headers { get; set; } = new();
    public ulong Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Stream { get; set; } = string.Empty;
}

/// <summary>
/// Telemetry message format from devices
/// </summary>
public class TelemetryMessage
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("lineId")]
    public string? LineId { get; set; }

    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("metrics")]
    public Dictionary<string, MetricValue>? Metrics { get; set; }

    // Alternative flat format
    [JsonPropertyName("metricName")]
    public string? MetricName { get; set; }

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("quality")]
    public int? Quality { get; set; }
}

/// <summary>
/// Metric value with optional unit
/// </summary>
public class MetricValue
{
    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("quality")]
    public int? Quality { get; set; }
}

/// <summary>
/// Event message format from devices
/// </summary>
public class EventMessage
{
    [JsonPropertyName("eventId")]
    public string? EventId { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("lineId")]
    public string? LineId { get; set; }

    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("causationId")]
    public string? CausationId { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }
}

/// <summary>
/// Quality inspection message format
/// </summary>
public class QualityInspectionMessage
{
    [JsonPropertyName("inspectionId")]
    public string? InspectionId { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("lineId")]
    public string? LineId { get; set; }

    [JsonPropertyName("batchId")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("result")]
    public string Result { get; set; } = "pass";

    [JsonPropertyName("defectType")]
    public string? DefectType { get; set; }

    [JsonPropertyName("defectDetails")]
    public JsonElement? DefectDetails { get; set; }

    [JsonPropertyName("measurements")]
    public JsonElement? Measurements { get; set; }

    [JsonPropertyName("imageRef")]
    public string? ImageRef { get; set; }

    [JsonPropertyName("operatorId")]
    public string? OperatorId { get; set; }
}
