namespace NatsWebSocketBridge.Historian.Configuration;

/// <summary>
/// Configuration for the Historian service
/// </summary>
public class HistorianOptions
{
    public const string SectionName = "Historian";

    /// <summary>
    /// TimescaleDB connection string
    /// </summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=manufacturing_history;Username=historian;Password=historian_secure_password";

    /// <summary>
    /// Batch size for bulk inserts
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time to wait for batch to fill before flushing (ms)
    /// </summary>
    public int BatchTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Number of parallel database writers
    /// </summary>
    public int WriterCount { get; set; } = 4;

    /// <summary>
    /// Whether to enable audit logging
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Whether to compute and store checksums for integrity
    /// </summary>
    public bool EnableIntegrityChecks { get; set; } = true;
}

/// <summary>
/// NATS connection configuration
/// </summary>
public class NatsOptions
{
    public const string SectionName = "Nats";

    /// <summary>
    /// NATS server URL
    /// </summary>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Client name for identification
    /// </summary>
    public string ClientName { get; set; } = "historian-service";
}

/// <summary>
/// JetStream consumer configuration for the Historian
/// </summary>
public class HistorianJetStreamOptions
{
    public const string SectionName = "JetStream";

    /// <summary>
    /// Consumer configurations for each data type
    /// </summary>
    public List<HistorianConsumerConfig> Consumers { get; set; } = new()
    {
        new HistorianConsumerConfig
        {
            Name = "historian-telemetry",
            StreamName = "TELEMETRY",
            FilterSubject = "factory.>",
            DataType = HistorianDataType.Telemetry
        },
        new HistorianConsumerConfig
        {
            Name = "historian-events",
            StreamName = "EVENTS",
            FilterSubject = "events.>",
            DataType = HistorianDataType.Event
        },
        new HistorianConsumerConfig
        {
            Name = "historian-alerts",
            StreamName = "ALERTS",
            FilterSubject = "alerts.>",
            DataType = HistorianDataType.Alert
        },
        new HistorianConsumerConfig
        {
            Name = "historian-quality",
            StreamName = "QUALITY",
            FilterSubject = "quality.>",
            DataType = HistorianDataType.QualityInspection
        }
    };

    /// <summary>
    /// Default batch size for fetching messages
    /// </summary>
    public int DefaultBatchSize { get; set; } = 100;

    /// <summary>
    /// Default fetch timeout
    /// </summary>
    public string FetchTimeout { get; set; } = "5s";

    /// <summary>
    /// Acknowledgement wait time
    /// </summary>
    public string AckWait { get; set; } = "60s";

    /// <summary>
    /// Maximum delivery attempts before dead-lettering
    /// </summary>
    public int MaxDeliver { get; set; } = 5;
}

/// <summary>
/// Consumer configuration for a specific data type
/// </summary>
public class HistorianConsumerConfig
{
    /// <summary>
    /// Consumer name (must be unique)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JetStream stream name to consume from
    /// </summary>
    public string StreamName { get; set; } = string.Empty;

    /// <summary>
    /// Subject filter pattern
    /// </summary>
    public string FilterSubject { get; set; } = string.Empty;

    /// <summary>
    /// Type of data this consumer handles
    /// </summary>
    public HistorianDataType DataType { get; set; }

    /// <summary>
    /// Whether this consumer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Types of data the Historian can process
/// </summary>
public enum HistorianDataType
{
    /// <summary>
    /// Time-series telemetry data (sensor readings)
    /// </summary>
    Telemetry,

    /// <summary>
    /// Discrete events (state changes, commands)
    /// </summary>
    Event,

    /// <summary>
    /// Alert/alarm data
    /// </summary>
    Alert,

    /// <summary>
    /// Quality inspection results
    /// </summary>
    QualityInspection,

    /// <summary>
    /// Batch/lot records
    /// </summary>
    Batch
}
