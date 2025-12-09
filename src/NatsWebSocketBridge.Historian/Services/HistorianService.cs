using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;
using NATS.Client.JetStream;
using NatsWebSocketBridge.Historian.Configuration;
using NatsWebSocketBridge.Historian.Models;
using Prometheus;

// Resolve ambiguity between NATS.Client.Options and Microsoft.Extensions.Options.Options
using NatsClientOptions = NATS.Client.Options;
using NatsConsumerConfig = NATS.Client.JetStream.ConsumerConfiguration;
using Duration = NATS.Client.Internals.Duration;
using HistorianNatsOptions = NatsWebSocketBridge.Historian.Configuration.NatsOptions;

namespace NatsWebSocketBridge.Historian.Services;

/// <summary>
/// Background service that consumes data from JetStream and persists to TimescaleDB (NATS.Client v1 API)
/// </summary>
public class HistorianService : BackgroundService
{
    private readonly ILogger<HistorianService> _logger;
    private readonly HistorianOptions _options;
    private readonly HistorianNatsOptions _natsOptions;
    private readonly HistorianJetStreamOptions _jsOptions;
    private readonly IHistorianRepository _repository;
    private readonly IChecksumService _checksumService;
    private readonly IAuditLogService _auditLogService;

    private IConnection? _natsConnection;
    private IJetStream? _jetStream;
    private IJetStreamManagement? _jetStreamManagement;

    // Batching channels for each data type
    private readonly Channel<TelemetryRecord> _telemetryChannel;
    private readonly Channel<EventRecord> _eventChannel;
    private readonly Channel<QualityInspectionRecord> _qualityChannel;

    // Metrics
    private static readonly Counter MessagesProcessed = Metrics.CreateCounter(
        "historian_messages_processed_total",
        "Total messages processed by the historian",
        new CounterConfiguration { LabelNames = new[] { "data_type", "status" } });

    private static readonly Counter RecordsInserted = Metrics.CreateCounter(
        "historian_records_inserted_total",
        "Total records inserted into TimescaleDB",
        new CounterConfiguration { LabelNames = new[] { "table" } });

    private static readonly Histogram ProcessingLatency = Metrics.CreateHistogram(
        "historian_processing_latency_seconds",
        "Time to process and persist messages",
        new HistogramConfiguration { LabelNames = new[] { "data_type" } });

    private static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "historian_queue_depth",
        "Current depth of the processing queue",
        new GaugeConfiguration { LabelNames = new[] { "data_type" } });

    public HistorianService(
        ILogger<HistorianService> logger,
        IOptions<HistorianOptions> options,
        IOptions<HistorianNatsOptions> natsOptions,
        IOptions<HistorianJetStreamOptions> jsOptions,
        IHistorianRepository repository,
        IChecksumService checksumService,
        IAuditLogService auditLogService)
    {
        _logger = logger;
        _options = options.Value;
        _natsOptions = natsOptions.Value;
        _jsOptions = jsOptions.Value;
        _repository = repository;
        _checksumService = checksumService;
        _auditLogService = auditLogService;

        // Create bounded channels for backpressure
        var channelOptions = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _telemetryChannel = Channel.CreateBounded<TelemetryRecord>(channelOptions);
        _eventChannel = Channel.CreateBounded<EventRecord>(channelOptions);
        _qualityChannel = Channel.CreateBounded<QualityInspectionRecord>(channelOptions);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Historian service starting...");

        try
        {
            ConnectToNats();
            InitializeJetStreamConsumers();

            // Start batch writers
            var writerTasks = new List<Task>
            {
                RunTelemetryWriterAsync(stoppingToken),
                RunEventWriterAsync(stoppingToken),
                RunQualityWriterAsync(stoppingToken)
            };

            // Start consumers for each configured stream
            var consumerTasks = _jsOptions.Consumers
                .Where(c => c.Enabled)
                .Select(c => RunConsumerAsync(c, stoppingToken))
                .ToList();

            _logger.LogInformation("Historian service started. Consuming from {ConsumerCount} streams",
                consumerTasks.Count);

            // Wait for all tasks (will complete when cancellation is requested)
            await Task.WhenAll(writerTasks.Concat(consumerTasks));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Historian service stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Historian service encountered a fatal error");
            throw;
        }
        finally
        {
            Cleanup();
        }
    }

    private void ConnectToNats()
    {
        _logger.LogInformation("Connecting to NATS at {Url}", _natsOptions.Url);

        var opts = ConnectionFactory.GetDefaultOptions();
        opts.Url = _natsOptions.Url;
        opts.Name = _natsOptions.ClientName;
        opts.AllowReconnect = true;
        opts.MaxReconnect = NatsClientOptions.ReconnectForever;
        opts.ReconnectWait = 1000;

        var factory = new ConnectionFactory();
        _natsConnection = factory.CreateConnection(opts);

        _jetStream = _natsConnection.CreateJetStreamContext();
        _jetStreamManagement = _natsConnection.CreateJetStreamManagementContext();

        _logger.LogInformation("Connected to NATS server");
    }

    private void InitializeJetStreamConsumers()
    {
        foreach (var consumerConfig in _jsOptions.Consumers.Where(c => c.Enabled))
        {
            try
            {
                // Check if stream exists
                try
                {
                    _jetStreamManagement!.GetStreamInfo(consumerConfig.StreamName);
                }
                catch (NATSJetStreamException ex) when (ex.ErrorCode == 10059)
                {
                    _logger.LogWarning("Stream {StreamName} not found, skipping consumer {ConsumerName}",
                        consumerConfig.StreamName, consumerConfig.Name);
                    continue;
                }

                // Create durable consumer if it doesn't exist
                try
                {
                    _jetStreamManagement!.GetConsumerInfo(consumerConfig.StreamName, consumerConfig.Name);
                    _logger.LogInformation("Using existing consumer {ConsumerName} on stream {StreamName}",
                        consumerConfig.Name, consumerConfig.StreamName);
                }
                catch (NATSJetStreamException ex) when (ex.ErrorCode == 10014)
                {
                    var config = NatsConsumerConfig.Builder()
                        .WithDurable(consumerConfig.Name)
                        .WithFilterSubject(consumerConfig.FilterSubject)
                        .WithAckPolicy(AckPolicy.Explicit)
                        .WithAckWait(Duration.OfMillis((long)ParseDuration(_jsOptions.AckWait).TotalMilliseconds))
                        .WithMaxDeliver(_jsOptions.MaxDeliver)
                        .WithDeliverPolicy(DeliverPolicy.All)
                        .Build();

                    _jetStreamManagement!.AddOrUpdateConsumer(consumerConfig.StreamName, config);
                    _logger.LogInformation("Created consumer {ConsumerName} on stream {StreamName}",
                        consumerConfig.Name, consumerConfig.StreamName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize consumer {ConsumerName}", consumerConfig.Name);
            }
        }
    }

    private async Task RunConsumerAsync(HistorianConsumerConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting consumer {ConsumerName} for {DataType}",
            config.Name, config.DataType);

        IJetStreamPullSubscription? subscription = null;

        try
        {
            var pullOpts = PullSubscribeOptions.Builder()
                .WithDurable(config.Name)
                .WithStream(config.StreamName)
                .Build();

            subscription = _jetStream!.PullSubscribe(config.FilterSubject, pullOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for consumer {ConsumerName}", config.Name);
            return;
        }

        var fetchTimeout = (int)ParseDuration(_jsOptions.FetchTimeout).TotalMilliseconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var messages = subscription.Fetch(_jsOptions.DefaultBatchSize, fetchTimeout);

                foreach (var msg in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    using var timer = ProcessingLatency.WithLabels(config.DataType.ToString()).NewTimer();

                    try
                    {
                        await ProcessMessageAsync(msg, config.DataType, cancellationToken);
                        msg.Ack();
                        MessagesProcessed.WithLabels(config.DataType.ToString(), "success").Inc();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process message from {Subject}", msg.Subject);
                        MessagesProcessed.WithLabels(config.DataType.ToString(), "error").Inc();

                        // NAK with delay for retry
                        msg.NakWithDelay(Duration.OfSeconds(5));
                    }
                }
            }
            catch (NATSTimeoutException)
            {
                // Expected when no messages are available
            }
            catch (NATSJetStreamException ex) when (ex.ErrorCode == 10059 || ex.ErrorCode == 10014)
            {
                _logger.LogWarning("Stream or consumer not found: {StreamName}/{ConsumerName}. Waiting...",
                    config.StreamName, config.Name);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in consumer {ConsumerName}", config.Name);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        subscription?.Unsubscribe();
        subscription?.Dispose();
    }

    private async Task ProcessMessageAsync(Msg msg, HistorianDataType dataType, CancellationToken cancellationToken)
    {
        var data = msg.Data ?? Array.Empty<byte>();
        var timestamp = msg.MetaData?.Timestamp ?? DateTime.UtcNow;

        switch (dataType)
        {
            case HistorianDataType.Telemetry:
                await ProcessTelemetryAsync(data, timestamp, msg.Subject, cancellationToken);
                break;

            case HistorianDataType.Event:
            case HistorianDataType.Alert:
                await ProcessEventAsync(data, timestamp, msg.Subject, dataType, cancellationToken);
                break;

            case HistorianDataType.QualityInspection:
                await ProcessQualityInspectionAsync(data, timestamp, msg.Subject, cancellationToken);
                break;

            default:
                _logger.LogWarning("Unknown data type: {DataType}", dataType);
                break;
        }
    }

    private async Task ProcessTelemetryAsync(byte[] data, DateTime timestamp, string subject, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<TelemetryMessage>(data);
            if (message == null) return;

            var records = new List<TelemetryRecord>();
            var time = message.Timestamp?.UtcDateTime ?? timestamp;
            var lineId = message.LineId ?? ExtractLineIdFromSubject(subject);

            if (message.Metrics != null)
            {
                // Multi-metric format
                foreach (var (metricName, metricValue) in message.Metrics)
                {
                    var record = new TelemetryRecord
                    {
                        Time = time,
                        DeviceId = message.DeviceId,
                        LineId = lineId,
                        BatchId = message.BatchId,
                        MetricName = metricName,
                        Value = metricValue.Value,
                        Unit = metricValue.Unit,
                        Quality = metricValue.Quality ?? 192
                    };

                    if (_options.EnableIntegrityChecks)
                    {
                        record.Checksum = _checksumService.ComputeChecksum(new
                        {
                            record.Time,
                            record.DeviceId,
                            record.MetricName,
                            record.Value
                        });
                    }

                    records.Add(record);
                }
            }
            else if (!string.IsNullOrEmpty(message.MetricName) && message.Value.HasValue)
            {
                // Single metric format
                var record = new TelemetryRecord
                {
                    Time = time,
                    DeviceId = message.DeviceId,
                    LineId = lineId,
                    BatchId = message.BatchId,
                    MetricName = message.MetricName,
                    Value = message.Value.Value,
                    Unit = message.Unit,
                    Quality = message.Quality ?? 192
                };

                if (_options.EnableIntegrityChecks)
                {
                    record.Checksum = _checksumService.ComputeChecksum(new
                    {
                        record.Time,
                        record.DeviceId,
                        record.MetricName,
                        record.Value
                    });
                }

                records.Add(record);
            }

            foreach (var record in records)
            {
                await _telemetryChannel.Writer.WriteAsync(record, cancellationToken);
            }

            QueueDepth.WithLabels("telemetry").Set(_telemetryChannel.Reader.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize telemetry message");
        }
    }

    private async Task ProcessEventAsync(byte[] data, DateTime timestamp, string subject, HistorianDataType dataType, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<EventMessage>(data);
            if (message == null) return;

            var record = new EventRecord
            {
                Id = string.IsNullOrEmpty(message.EventId) ? Guid.NewGuid() : Guid.Parse(message.EventId),
                Time = message.Timestamp?.UtcDateTime ?? timestamp,
                EventType = message.EventType,
                Severity = message.Severity ?? (dataType == HistorianDataType.Alert ? "warning" : "info"),
                DeviceId = message.DeviceId,
                LineId = message.LineId ?? ExtractLineIdFromSubject(subject),
                BatchId = message.BatchId,
                Payload = message.Payload.HasValue
                    ? JsonDocument.Parse(message.Payload.Value.GetRawText())
                    : null,
                CorrelationId = string.IsNullOrEmpty(message.CorrelationId) ? null : Guid.Parse(message.CorrelationId),
                CausationId = string.IsNullOrEmpty(message.CausationId) ? null : Guid.Parse(message.CausationId),
                UserId = message.UserId
            };

            if (_options.EnableIntegrityChecks)
            {
                record.Checksum = _checksumService.ComputeChecksum(new
                {
                    record.Time,
                    record.EventType,
                    record.DeviceId,
                    record.Severity
                });
            }

            await _eventChannel.Writer.WriteAsync(record, cancellationToken);
            QueueDepth.WithLabels("events").Set(_eventChannel.Reader.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize event message");
        }
    }

    private async Task ProcessQualityInspectionAsync(byte[] data, DateTime timestamp, string subject, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<QualityInspectionMessage>(data);
            if (message == null) return;

            var record = new QualityInspectionRecord
            {
                Id = string.IsNullOrEmpty(message.InspectionId) ? Guid.NewGuid() : Guid.Parse(message.InspectionId),
                Time = message.Timestamp?.UtcDateTime ?? timestamp,
                DeviceId = message.DeviceId,
                LineId = message.LineId ?? ExtractLineIdFromSubject(subject),
                BatchId = message.BatchId,
                ProductId = message.ProductId,
                Result = message.Result,
                DefectType = message.DefectType,
                DefectDetails = message.DefectDetails.HasValue
                    ? JsonDocument.Parse(message.DefectDetails.Value.GetRawText())
                    : null,
                Measurements = message.Measurements.HasValue
                    ? JsonDocument.Parse(message.Measurements.Value.GetRawText())
                    : null,
                ImageRef = message.ImageRef,
                OperatorId = message.OperatorId
            };

            if (_options.EnableIntegrityChecks)
            {
                record.Checksum = _checksumService.ComputeChecksum(new
                {
                    record.Time,
                    record.DeviceId,
                    record.BatchId,
                    record.Result
                });
            }

            await _qualityChannel.Writer.WriteAsync(record, cancellationToken);
            QueueDepth.WithLabels("quality").Set(_qualityChannel.Reader.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize quality inspection message");
        }
    }

    #region Batch Writers

    private async Task RunTelemetryWriterAsync(CancellationToken cancellationToken)
    {
        var batch = new List<TelemetryRecord>(_options.BatchSize);
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.BatchTimeoutMs));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var timerTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();

                while (batch.Count < _options.BatchSize)
                {
                    var readTask = _telemetryChannel.Reader.ReadAsync(cancellationToken).AsTask();
                    var completed = await Task.WhenAny(readTask, timerTask);

                    if (completed == timerTask)
                        break;

                    batch.Add(await readTask);
                }

                if (batch.Count > 0)
                {
                    var count = await _repository.InsertTelemetryBatchAsync(batch, cancellationToken);
                    RecordsInserted.WithLabels("telemetry").Inc(count);

                    // Audit log for batch insert
                    if (_options.EnableAuditLogging && count > 0)
                    {
                        await _auditLogService.LogAsync(
                            "INGEST",
                            "telemetry",
                            null,
                            null,
                            new { RecordCount = count, BatchId = batch.FirstOrDefault()?.BatchId },
                            "historian-service",
                            "Batch telemetry data ingestion");
                    }

                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in telemetry writer");
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            await _repository.InsertTelemetryBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task RunEventWriterAsync(CancellationToken cancellationToken)
    {
        var batch = new List<EventRecord>(_options.BatchSize);
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.BatchTimeoutMs));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var timerTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();

                while (batch.Count < _options.BatchSize)
                {
                    var readTask = _eventChannel.Reader.ReadAsync(cancellationToken).AsTask();
                    var completed = await Task.WhenAny(readTask, timerTask);

                    if (completed == timerTask)
                        break;

                    batch.Add(await readTask);
                }

                if (batch.Count > 0)
                {
                    var count = await _repository.InsertEventBatchAsync(batch, cancellationToken);
                    RecordsInserted.WithLabels("events").Inc(count);

                    // Audit log for batch insert
                    if (_options.EnableAuditLogging && count > 0)
                    {
                        await _auditLogService.LogAsync(
                            "INGEST",
                            "events",
                            null,
                            null,
                            new { RecordCount = count, BatchId = batch.FirstOrDefault()?.BatchId },
                            "historian-service",
                            "Batch event data ingestion");
                    }

                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in event writer");
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            await _repository.InsertEventBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task RunQualityWriterAsync(CancellationToken cancellationToken)
    {
        var batch = new List<QualityInspectionRecord>(_options.BatchSize);
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.BatchTimeoutMs));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var timerTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();

                while (batch.Count < _options.BatchSize)
                {
                    var readTask = _qualityChannel.Reader.ReadAsync(cancellationToken).AsTask();
                    var completed = await Task.WhenAny(readTask, timerTask);

                    if (completed == timerTask)
                        break;

                    batch.Add(await readTask);
                }

                if (batch.Count > 0)
                {
                    var count = await _repository.InsertQualityInspectionBatchAsync(batch, cancellationToken);
                    RecordsInserted.WithLabels("quality_inspections").Inc(count);

                    // Audit log for batch insert
                    if (_options.EnableAuditLogging && count > 0)
                    {
                        await _auditLogService.LogAsync(
                            "INGEST",
                            "quality_inspections",
                            null,
                            null,
                            new { RecordCount = count, BatchId = batch.FirstOrDefault()?.BatchId },
                            "historian-service",
                            "Batch quality inspection data ingestion");
                    }

                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in quality writer");
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            await _repository.InsertQualityInspectionBatchAsync(batch, CancellationToken.None);
        }
    }

    #endregion

    #region Helpers

    private static string ExtractLineIdFromSubject(string subject)
    {
        // Extract line ID from subject like "factory.line1.sensor.temperature"
        var parts = subject.Split('.');
        if (parts.Length >= 2 && parts[0] == "factory")
        {
            return parts[1];
        }
        return "unknown";
    }

    private static TimeSpan ParseDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return TimeSpan.Zero;

        duration = duration.Trim().ToLowerInvariant();
        var totalTicks = 0L;
        var currentNumber = "";

        foreach (var c in duration)
        {
            if (char.IsDigit(c) || c == '.')
            {
                currentNumber += c;
            }
            else if (!string.IsNullOrEmpty(currentNumber))
            {
                var value = double.Parse(currentNumber);
                totalTicks += c switch
                {
                    'd' => TimeSpan.FromDays(value).Ticks,
                    'h' => TimeSpan.FromHours(value).Ticks,
                    'm' => TimeSpan.FromMinutes(value).Ticks,
                    's' => TimeSpan.FromSeconds(value).Ticks,
                    _ => TimeSpan.FromMilliseconds(value).Ticks
                };
                currentNumber = "";
            }
        }

        if (!string.IsNullOrEmpty(currentNumber))
        {
            totalTicks += TimeSpan.FromMilliseconds(double.Parse(currentNumber)).Ticks;
        }

        return TimeSpan.FromTicks(totalTicks);
    }

    private void Cleanup()
    {
        _telemetryChannel.Writer.Complete();
        _eventChannel.Writer.Complete();
        _qualityChannel.Writer.Complete();

        _natsConnection?.Drain();
        _natsConnection?.Close();
        _natsConnection?.Dispose();

        _logger.LogInformation("Historian service stopped");
    }

    #endregion
}
