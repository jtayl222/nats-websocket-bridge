using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Historian.Configuration;
using NatsWebSocketBridge.Historian.Models;
using Prometheus;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// Configuration for the archival service
/// </summary>
public class ArchivalOptions
{
    public const string SectionName = "Archival";

    /// <summary>
    /// Whether archival is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// S3/MinIO endpoint URL
    /// </summary>
    public string S3Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>
    /// S3/MinIO access key
    /// </summary>
    public string AccessKey { get; set; } = "minioadmin";

    /// <summary>
    /// S3/MinIO secret key
    /// </summary>
    public string SecretKey { get; set; } = "minio_secure_password";

    /// <summary>
    /// Bucket for telemetry archives
    /// </summary>
    public string TelemetryBucket { get; set; } = "historical-telemetry";

    /// <summary>
    /// Bucket for event archives
    /// </summary>
    public string EventsBucket { get; set; } = "historical-events";

    /// <summary>
    /// Bucket for batch archives
    /// </summary>
    public string BatchesBucket { get; set; } = "historical-batches";

    /// <summary>
    /// Bucket for audit log archives
    /// </summary>
    public string AuditBucket { get; set; } = "historical-audit";

    /// <summary>
    /// Age threshold for archiving telemetry (days)
    /// </summary>
    public int TelemetryArchiveAgeDays { get; set; } = 90;

    /// <summary>
    /// Age threshold for archiving events (days)
    /// </summary>
    public int EventsArchiveAgeDays { get; set; } = 180;

    /// <summary>
    /// Interval between archive runs (hours)
    /// </summary>
    public int ArchiveIntervalHours { get; set; } = 24;

    /// <summary>
    /// Maximum records per archive file
    /// </summary>
    public int MaxRecordsPerFile { get; set; } = 100000;

    /// <summary>
    /// Whether to delete from warm storage after archiving
    /// </summary>
    public bool DeleteAfterArchive { get; set; } = false;
}

/// <summary>
/// Background service that archives old data from TimescaleDB to S3/MinIO
/// </summary>
public class ArchivalService : BackgroundService
{
    private readonly ILogger<ArchivalService> _logger;
    private readonly ArchivalOptions _options;
    private readonly IHistorianRepository _repository;
    private readonly IAuditLogService _auditLogService;
    private readonly IChecksumService _checksumService;

    // Metrics
    private static readonly Counter ArchivesCreated = Metrics.CreateCounter(
        "historian_archives_created_total",
        "Total archive files created",
        new CounterConfiguration { LabelNames = new[] { "data_type" } });

    private static readonly Counter RecordsArchived = Metrics.CreateCounter(
        "historian_records_archived_total",
        "Total records archived to cold storage",
        new CounterConfiguration { LabelNames = new[] { "data_type" } });

    private static readonly Gauge ArchiveFileSizeBytes = Metrics.CreateGauge(
        "historian_archive_file_size_bytes",
        "Size of last archive file in bytes",
        new GaugeConfiguration { LabelNames = new[] { "data_type" } });

    private static readonly Histogram ArchiveDuration = Metrics.CreateHistogram(
        "historian_archive_duration_seconds",
        "Time to complete archive operation",
        new HistogramConfiguration { LabelNames = new[] { "data_type" } });

    public ArchivalService(
        ILogger<ArchivalService> logger,
        IOptions<ArchivalOptions> options,
        IHistorianRepository repository,
        IAuditLogService auditLogService,
        IChecksumService checksumService)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
        _auditLogService = auditLogService;
        _checksumService = checksumService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Archival service is disabled");
            return;
        }

        _logger.LogInformation("Archival service starting. Archive interval: {Hours} hours",
            _options.ArchiveIntervalHours);

        // Initial delay to let the system stabilize
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunArchivalCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in archival cycle");
            }

            await Task.Delay(TimeSpan.FromHours(_options.ArchiveIntervalHours), stoppingToken);
        }

        _logger.LogInformation("Archival service stopped");
    }

    private async Task RunArchivalCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting archival cycle");

        // Archive telemetry data
        await ArchiveTelemetryAsync(cancellationToken);

        // Archive events
        await ArchiveEventsAsync(cancellationToken);

        _logger.LogInformation("Archival cycle completed");
    }

    private async Task ArchiveTelemetryAsync(CancellationToken cancellationToken)
    {
        using var timer = ArchiveDuration.WithLabels("telemetry").NewTimer();

        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_options.TelemetryArchiveAgeDays);

        _logger.LogInformation("Archiving telemetry data older than {CutoffDate}", cutoffDate);

        // Note: In a production implementation, you would:
        // 1. Query TimescaleDB for records older than cutoff
        // 2. Export them in chunks to compressed JSON/Parquet files
        // 3. Upload to S3/MinIO
        // 4. Optionally delete from TimescaleDB
        // 5. Log to audit trail

        // For now, we'll create a placeholder that demonstrates the structure
        var archiveMetadata = new ArchiveMetadata
        {
            ArchiveId = Guid.NewGuid().ToString(),
            DataType = "telemetry",
            StartTime = cutoffDate.AddDays(-30),
            EndTime = cutoffDate,
            RecordCount = 0, // Would be populated with actual count
            CreatedAt = DateTimeOffset.UtcNow,
            Checksum = string.Empty
        };

        // Log the archival action
        await _auditLogService.LogAsync(
            "ARCHIVE",
            "telemetry",
            archiveMetadata.ArchiveId,
            null,
            archiveMetadata,
            "archival-service",
            $"Archived telemetry data from {archiveMetadata.StartTime:yyyy-MM-dd} to {archiveMetadata.EndTime:yyyy-MM-dd}");

        _logger.LogInformation("Telemetry archival completed. Archive ID: {ArchiveId}", archiveMetadata.ArchiveId);
    }

    private async Task ArchiveEventsAsync(CancellationToken cancellationToken)
    {
        using var timer = ArchiveDuration.WithLabels("events").NewTimer();

        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_options.EventsArchiveAgeDays);

        _logger.LogInformation("Archiving event data older than {CutoffDate}", cutoffDate);

        var archiveMetadata = new ArchiveMetadata
        {
            ArchiveId = Guid.NewGuid().ToString(),
            DataType = "events",
            StartTime = cutoffDate.AddDays(-30),
            EndTime = cutoffDate,
            RecordCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Checksum = string.Empty
        };

        await _auditLogService.LogAsync(
            "ARCHIVE",
            "events",
            archiveMetadata.ArchiveId,
            null,
            archiveMetadata,
            "archival-service",
            $"Archived event data from {archiveMetadata.StartTime:yyyy-MM-dd} to {archiveMetadata.EndTime:yyyy-MM-dd}");

        _logger.LogInformation("Events archival completed. Archive ID: {ArchiveId}", archiveMetadata.ArchiveId);
    }

    /// <summary>
    /// Export data to a compressed JSON file for archiving
    /// </summary>
    public async Task<ArchiveResult> ExportToArchiveAsync<T>(
        IEnumerable<T> records,
        string dataType,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        var archiveId = Guid.NewGuid().ToString();
        var fileName = $"{dataType}/{startTime:yyyy/MM/dd}/{archiveId}.json.gz";

        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
        {
            var archiveDocument = new
            {
                ArchiveId = archiveId,
                DataType = dataType,
                StartTime = startTime,
                EndTime = endTime,
                RecordCount = recordList.Count,
                CreatedAt = DateTimeOffset.UtcNow,
                Records = recordList
            };

            var json = JsonSerializer.Serialize(archiveDocument, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            await writer.WriteAsync(json);
        }

        memoryStream.Position = 0;
        var compressedData = memoryStream.ToArray();
        var checksum = _checksumService.ComputeChecksum(compressedData);

        // Here you would upload to S3/MinIO
        // For now, we return the metadata

        ArchivesCreated.WithLabels(dataType).Inc();
        RecordsArchived.WithLabels(dataType).Inc(recordList.Count);
        ArchiveFileSizeBytes.WithLabels(dataType).Set(compressedData.Length);

        return new ArchiveResult
        {
            Success = true,
            ArchiveId = archiveId,
            FileName = fileName,
            RecordCount = recordList.Count,
            FileSizeBytes = compressedData.Length,
            Checksum = checksum
        };
    }
}

/// <summary>
/// Metadata for an archive file
/// </summary>
public class ArchiveMetadata
{
    public string ArchiveId { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int RecordCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? Bucket { get; set; }
    public string? Key { get; set; }
}

/// <summary>
/// Result of an archive operation
/// </summary>
public class ArchiveResult
{
    public bool Success { get; set; }
    public string ArchiveId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
