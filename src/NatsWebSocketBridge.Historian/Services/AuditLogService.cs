using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Historian.Configuration;
using NatsWebSocketBridge.Historian.Models;
using Prometheus;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// Service for FDA 21 CFR Part 11 compliant audit logging
/// Maintains a tamper-evident chain of audit entries
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Log a CREATE action
    /// </summary>
    Task LogCreateAsync(string resourceType, string resourceId, object newValue, string? userId = null, string? reason = null);

    /// <summary>
    /// Log a READ action (for sensitive data access)
    /// </summary>
    Task LogReadAsync(string resourceType, string resourceId, string? userId = null, string? reason = null);

    /// <summary>
    /// Log an UPDATE action
    /// </summary>
    Task LogUpdateAsync(string resourceType, string resourceId, object? oldValue, object newValue, string? userId = null, string? reason = null);

    /// <summary>
    /// Log a DELETE action
    /// </summary>
    Task LogDeleteAsync(string resourceType, string resourceId, object? oldValue, string? userId = null, string? reason = null);

    /// <summary>
    /// Log an EXPORT action
    /// </summary>
    Task LogExportAsync(string resourceType, string? resourceId, string? userId = null, string? reason = null, object? metadata = null);

    /// <summary>
    /// Log a custom action
    /// </summary>
    Task LogAsync(string action, string resourceType, string? resourceId = null, object? oldValue = null, object? newValue = null, string? userId = null, string? reason = null, object? metadata = null);

    /// <summary>
    /// Verify the integrity of the audit log chain
    /// </summary>
    Task<AuditIntegrityResult> VerifyIntegrityAsync(long? startId = null, long? endId = null);
}

/// <summary>
/// Result of audit log integrity verification
/// </summary>
public class AuditIntegrityResult
{
    public bool IsValid { get; set; }
    public int TotalRecords { get; set; }
    public int ValidRecords { get; set; }
    public int TamperedRecords { get; set; }
    public List<long> TamperedIds { get; set; } = new();
    public DateTimeOffset VerificationTime { get; set; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Implementation of FDA 21 CFR Part 11 compliant audit logging
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly ILogger<AuditLogService> _logger;
    private readonly IHistorianRepository _repository;
    private readonly IChecksumService _checksumService;
    private readonly HistorianOptions _options;

    // Thread-safe hash chain tracking
    private readonly SemaphoreSlim _hashLock = new(1, 1);
    private string _lastHash = "GENESIS";

    // Metrics
    private static readonly Counter AuditEntriesCreated = Metrics.CreateCounter(
        "historian_audit_entries_total",
        "Total audit log entries created",
        new CounterConfiguration { LabelNames = new[] { "action", "resource_type" } });

    private static readonly Counter AuditErrors = Metrics.CreateCounter(
        "historian_audit_errors_total",
        "Audit logging errors",
        new CounterConfiguration { LabelNames = new[] { "error_type" } });

    private static readonly Histogram AuditLatency = Metrics.CreateHistogram(
        "historian_audit_latency_seconds",
        "Time to write audit log entries");

    public AuditLogService(
        ILogger<AuditLogService> logger,
        IHistorianRepository repository,
        IChecksumService checksumService,
        IOptions<HistorianOptions> options)
    {
        _logger = logger;
        _repository = repository;
        _checksumService = checksumService;
        _options = options.Value;
    }

    public Task LogCreateAsync(string resourceType, string resourceId, object newValue, string? userId = null, string? reason = null)
    {
        return LogAsync("CREATE", resourceType, resourceId, null, newValue, userId, reason);
    }

    public Task LogReadAsync(string resourceType, string resourceId, string? userId = null, string? reason = null)
    {
        return LogAsync("READ", resourceType, resourceId, null, null, userId, reason);
    }

    public Task LogUpdateAsync(string resourceType, string resourceId, object? oldValue, object newValue, string? userId = null, string? reason = null)
    {
        return LogAsync("UPDATE", resourceType, resourceId, oldValue, newValue, userId, reason);
    }

    public Task LogDeleteAsync(string resourceType, string resourceId, object? oldValue, string? userId = null, string? reason = null)
    {
        return LogAsync("DELETE", resourceType, resourceId, oldValue, null, userId, reason);
    }

    public Task LogExportAsync(string resourceType, string? resourceId, string? userId = null, string? reason = null, object? metadata = null)
    {
        return LogAsync("EXPORT", resourceType, resourceId, null, null, userId, reason, metadata);
    }

    public async Task LogAsync(
        string action,
        string resourceType,
        string? resourceId = null,
        object? oldValue = null,
        object? newValue = null,
        string? userId = null,
        string? reason = null,
        object? metadata = null)
    {
        if (!_options.EnableAuditLogging)
        {
            return;
        }

        using var timer = AuditLatency.NewTimer();

        try
        {
            await _hashLock.WaitAsync();
            try
            {
                // Get the previous hash for chain integrity
                var previousHash = _lastHash;
                if (previousHash == "GENESIS")
                {
                    // First entry or restart - get from database
                    previousHash = await _repository.GetLastAuditHashAsync();
                    _lastHash = previousHash;
                }

                // Create the audit entry
                var entry = new AuditLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = action,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    UserId = userId,
                    OldValue = oldValue != null ? JsonDocument.Parse(JsonSerializer.Serialize(oldValue)) : null,
                    NewValue = newValue != null ? JsonDocument.Parse(JsonSerializer.Serialize(newValue)) : null,
                    Metadata = metadata != null ? JsonDocument.Parse(JsonSerializer.Serialize(metadata)) : null,
                    Reason = reason,
                    PreviousHash = previousHash
                };

                // Compute checksum for this entry (includes previous hash for chain integrity)
                var checksumData = new
                {
                    entry.Timestamp,
                    entry.Action,
                    entry.ResourceType,
                    entry.ResourceId,
                    entry.UserId,
                    OldValue = entry.OldValue?.RootElement.GetRawText(),
                    NewValue = entry.NewValue?.RootElement.GetRawText(),
                    entry.Reason,
                    entry.PreviousHash
                };

                entry.Checksum = _checksumService.ComputeChecksum(checksumData);

                // Persist to database
                await _repository.InsertAuditLogAsync(entry);

                // Update chain
                _lastHash = entry.Checksum;

                AuditEntriesCreated.WithLabels(action, resourceType).Inc();

                _logger.LogDebug("Audit log: {Action} on {ResourceType}/{ResourceId} by {UserId}",
                    action, resourceType, resourceId ?? "N/A", userId ?? "system");
            }
            finally
            {
                _hashLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry: {Action} on {ResourceType}",
                action, resourceType);
            AuditErrors.WithLabels("write_failure").Inc();

            // Don't throw - audit log failures shouldn't break the main flow
            // But in a real compliance scenario, you might want to halt operations
        }
    }

    public async Task<AuditIntegrityResult> VerifyIntegrityAsync(long? startId = null, long? endId = null)
    {
        var result = new AuditIntegrityResult();

        try
        {
            // Get all audit entries in the range
            var entries = await _repository.GetAuditLogAsync("*", null, null, null);
            var entryList = entries.ToList();

            result.TotalRecords = entryList.Count;

            if (entryList.Count == 0)
            {
                result.IsValid = true;
                return result;
            }

            string expectedPreviousHash = "GENESIS";

            foreach (var entry in entryList.OrderBy(e => e.Id))
            {
                // Skip entries outside the range if specified
                if (startId.HasValue && entry.Id < startId.Value) continue;
                if (endId.HasValue && entry.Id > endId.Value) break;

                // Verify chain integrity
                if (entry.PreviousHash != expectedPreviousHash)
                {
                    result.TamperedIds.Add(entry.Id);
                    result.TamperedRecords++;

                    _logger.LogWarning(
                        "Audit log integrity violation at ID {Id}: expected previous hash {Expected}, got {Actual}",
                        entry.Id, expectedPreviousHash, entry.PreviousHash);
                }
                else
                {
                    result.ValidRecords++;
                }

                // Verify checksum
                var checksumData = new
                {
                    entry.Timestamp,
                    entry.Action,
                    entry.ResourceType,
                    entry.ResourceId,
                    entry.UserId,
                    OldValue = entry.OldValue?.RootElement.GetRawText(),
                    NewValue = entry.NewValue?.RootElement.GetRawText(),
                    entry.Reason,
                    entry.PreviousHash
                };

                var expectedChecksum = _checksumService.ComputeChecksum(checksumData);
                if (entry.Checksum != expectedChecksum)
                {
                    if (!result.TamperedIds.Contains(entry.Id))
                    {
                        result.TamperedIds.Add(entry.Id);
                        result.TamperedRecords++;
                        result.ValidRecords--;
                    }

                    _logger.LogWarning(
                        "Audit log checksum mismatch at ID {Id}: record may have been tampered",
                        entry.Id);
                }

                expectedPreviousHash = entry.Checksum;
            }

            result.IsValid = result.TamperedRecords == 0;

            if (!result.IsValid)
            {
                _logger.LogError(
                    "Audit log integrity verification FAILED: {TamperedCount} of {TotalCount} records tampered",
                    result.TamperedRecords, result.TotalRecords);

                AuditErrors.WithLabels("integrity_violation").Inc();
            }
            else
            {
                _logger.LogInformation(
                    "Audit log integrity verification PASSED: {ValidCount} records verified",
                    result.ValidRecords);
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Audit log integrity verification failed with exception");
            AuditErrors.WithLabels("verification_error").Inc();
        }

        return result;
    }
}
