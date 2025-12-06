namespace NatsWebSocketBridge.Gateway.Configuration;

/// <summary>
/// Configuration for JetStream streams and consumers
/// </summary>
public class JetStreamOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "JetStream";

    /// <summary>
    /// Whether JetStream is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Stream configurations
    /// </summary>
    public List<StreamConfiguration> Streams { get; set; } = new();

    /// <summary>
    /// Consumer configurations
    /// </summary>
    public List<ConsumerConfiguration> Consumers { get; set; } = new();

    /// <summary>
    /// Default retry policy for publish operations
    /// </summary>
    public RetryPolicyConfiguration PublishRetryPolicy { get; set; } = new();

    /// <summary>
    /// Default consumer options
    /// </summary>
    public DefaultConsumerOptions DefaultConsumerOptions { get; set; } = new();
}

/// <summary>
/// Configuration for a JetStream stream
/// </summary>
public class StreamConfiguration
{
    /// <summary>
    /// Unique name for the stream
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Subject patterns this stream captures (e.g., "factory.sensor.*")
    /// </summary>
    public List<string> Subjects { get; set; } = new();

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Retention policy: Limits, Interest, or WorkQueue
    /// </summary>
    public StreamRetentionPolicy Retention { get; set; } = StreamRetentionPolicy.Limits;

    /// <summary>
    /// Storage type: Memory or File
    /// </summary>
    public StreamStorageType Storage { get; set; } = StreamStorageType.Memory;

    /// <summary>
    /// Maximum age of messages (e.g., "24h", "7d", "1h30m")
    /// </summary>
    public string MaxAge { get; set; } = "24h";

    /// <summary>
    /// Maximum number of messages
    /// </summary>
    public long MaxMessages { get; set; } = 1_000_000;

    /// <summary>
    /// Maximum total bytes
    /// </summary>
    public long MaxBytes { get; set; } = 1024 * 1024 * 1024; // 1GB

    /// <summary>
    /// Maximum message size in bytes
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Number of replicas for HA (1, 3, or 5)
    /// </summary>
    public int Replicas { get; set; } = 1;

    /// <summary>
    /// Discard policy when limits are reached: Old or New
    /// </summary>
    public StreamDiscardPolicy Discard { get; set; } = StreamDiscardPolicy.Old;

    /// <summary>
    /// Allow direct get operations
    /// </summary>
    public bool AllowDirect { get; set; } = true;

    /// <summary>
    /// Allow rollup headers
    /// </summary>
    public bool AllowRollup { get; set; } = false;

    /// <summary>
    /// Deny deletes
    /// </summary>
    public bool DenyDelete { get; set; } = false;

    /// <summary>
    /// Deny purges
    /// </summary>
    public bool DenyPurge { get; set; } = false;
}

/// <summary>
/// Stream retention policy
/// </summary>
public enum StreamRetentionPolicy
{
    /// <summary>
    /// Retain messages based on limits (max age, max messages, max bytes)
    /// </summary>
    Limits,

    /// <summary>
    /// Retain messages while there are active consumers interested
    /// </summary>
    Interest,

    /// <summary>
    /// Work queue semantics - message removed after first ack
    /// </summary>
    WorkQueue
}

/// <summary>
/// Stream storage type
/// </summary>
public enum StreamStorageType
{
    /// <summary>
    /// Store in memory (faster, not durable across restarts)
    /// </summary>
    Memory,

    /// <summary>
    /// Store on disk (durable)
    /// </summary>
    File
}

/// <summary>
/// Stream discard policy
/// </summary>
public enum StreamDiscardPolicy
{
    /// <summary>
    /// Discard old messages when limits are reached
    /// </summary>
    Old,

    /// <summary>
    /// Reject new messages when limits are reached
    /// </summary>
    New
}

/// <summary>
/// Configuration for a JetStream consumer
/// </summary>
public class ConsumerConfiguration
{
    /// <summary>
    /// Unique name for the consumer
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of the stream this consumer reads from
    /// </summary>
    public string StreamName { get; set; } = string.Empty;

    /// <summary>
    /// Durable name (makes the consumer persistent)
    /// </summary>
    public string DurableName { get; set; } = string.Empty;

    /// <summary>
    /// Filter subject for this consumer
    /// </summary>
    public string FilterSubject { get; set; } = string.Empty;

    /// <summary>
    /// Delivery policy for new consumers
    /// </summary>
    public DeliveryPolicy DeliveryPolicy { get; set; } = DeliveryPolicy.All;

    /// <summary>
    /// Acknowledgement policy
    /// </summary>
    public AckPolicy AckPolicy { get; set; } = AckPolicy.Explicit;

    /// <summary>
    /// How long to wait for an ack before redelivery
    /// </summary>
    public string AckWait { get; set; } = "30s";

    /// <summary>
    /// Maximum number of delivery attempts
    /// </summary>
    public int MaxDeliver { get; set; } = 5;

    /// <summary>
    /// Maximum number of pending acks
    /// </summary>
    public int MaxAckPending { get; set; } = 1000;

    /// <summary>
    /// Replay policy
    /// </summary>
    public ReplayPolicy ReplayPolicy { get; set; } = ReplayPolicy.Instant;

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a push or pull consumer
    /// </summary>
    public ConsumerType Type { get; set; } = ConsumerType.Pull;

    /// <summary>
    /// For push consumers, the delivery subject
    /// </summary>
    public string DeliverSubject { get; set; } = string.Empty;

    /// <summary>
    /// For push consumers, the delivery group (for load balancing)
    /// </summary>
    public string DeliverGroup { get; set; } = string.Empty;

    /// <summary>
    /// Idle heartbeat interval for push consumers
    /// </summary>
    public string IdleHeartbeat { get; set; } = "30s";

    /// <summary>
    /// Flow control for push consumers
    /// </summary>
    public bool FlowControl { get; set; } = false;
}

/// <summary>
/// Consumer type
/// </summary>
public enum ConsumerType
{
    /// <summary>
    /// Pull-based consumer (client pulls messages)
    /// </summary>
    Pull,

    /// <summary>
    /// Push-based consumer (server pushes messages)
    /// </summary>
    Push
}

/// <summary>
/// Delivery policy for consumers
/// </summary>
public enum DeliveryPolicy
{
    /// <summary>
    /// Deliver all available messages
    /// </summary>
    All,

    /// <summary>
    /// Deliver starting from the last message
    /// </summary>
    Last,

    /// <summary>
    /// Deliver only new messages (arriving after consumer creation)
    /// </summary>
    New,

    /// <summary>
    /// Deliver from a specific sequence number
    /// </summary>
    ByStartSequence,

    /// <summary>
    /// Deliver from a specific time
    /// </summary>
    ByStartTime,

    /// <summary>
    /// Deliver last message per subject
    /// </summary>
    LastPerSubject
}

/// <summary>
/// Acknowledgement policy
/// </summary>
public enum AckPolicy
{
    /// <summary>
    /// No acknowledgement required
    /// </summary>
    None,

    /// <summary>
    /// All messages must be explicitly acknowledged
    /// </summary>
    Explicit,

    /// <summary>
    /// Acknowledge all messages up to and including the current one
    /// </summary>
    All
}

/// <summary>
/// Replay policy for consumers
/// </summary>
public enum ReplayPolicy
{
    /// <summary>
    /// Replay messages as fast as possible
    /// </summary>
    Instant,

    /// <summary>
    /// Replay messages at original rate
    /// </summary>
    Original
}

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryPolicyConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries
    /// </summary>
    public string InitialDelay { get; set; } = "100ms";

    /// <summary>
    /// Maximum delay between retries
    /// </summary>
    public string MaxDelay { get; set; } = "5s";

    /// <summary>
    /// Multiplier for exponential backoff
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Whether to add jitter to retry delays
    /// </summary>
    public bool AddJitter { get; set; } = true;
}

/// <summary>
/// Default consumer options
/// </summary>
public class DefaultConsumerOptions
{
    /// <summary>
    /// Default ack wait time
    /// </summary>
    public string AckWait { get; set; } = "30s";

    /// <summary>
    /// Default max deliver attempts
    /// </summary>
    public int MaxDeliver { get; set; } = 5;

    /// <summary>
    /// Default max ack pending
    /// </summary>
    public int MaxAckPending { get; set; } = 1000;

    /// <summary>
    /// Default batch size for pull consumers
    /// </summary>
    public int DefaultBatchSize { get; set; } = 100;

    /// <summary>
    /// Default fetch timeout
    /// </summary>
    public string FetchTimeout { get; set; } = "5s";
}

/// <summary>
/// Replay options for device subscriptions
/// </summary>
public class ReplayOptions
{
    /// <summary>
    /// Replay mode
    /// </summary>
    public ReplayMode Mode { get; set; } = ReplayMode.New;

    /// <summary>
    /// For ByStartSequence mode, the starting sequence number
    /// </summary>
    public ulong? StartSequence { get; set; }

    /// <summary>
    /// For ByStartTime mode, the starting timestamp
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Whether to resume from last ack on reconnect
    /// </summary>
    public bool ResumeFromLastAck { get; set; } = true;
}

/// <summary>
/// Replay mode for device subscriptions
/// </summary>
public enum ReplayMode
{
    /// <summary>
    /// Only receive new messages
    /// </summary>
    New,

    /// <summary>
    /// Receive all available messages
    /// </summary>
    All,

    /// <summary>
    /// Receive from last message
    /// </summary>
    Last,

    /// <summary>
    /// Receive from a specific sequence
    /// </summary>
    FromSequence,

    /// <summary>
    /// Receive from a specific time
    /// </summary>
    FromTime,

    /// <summary>
    /// Resume from last acknowledged message
    /// </summary>
    ResumeFromLastAck
}

/// <summary>
/// Helper class for parsing duration strings
/// </summary>
public static class DurationParser
{
    /// <summary>
    /// Parse a duration string like "30s", "5m", "1h", "24h", "7d" to TimeSpan
    /// </summary>
    public static TimeSpan Parse(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return TimeSpan.Zero;

        duration = duration.Trim().ToLowerInvariant();

        // Handle compound durations like "1h30m"
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

        // Handle bare numbers (assume milliseconds)
        if (!string.IsNullOrEmpty(currentNumber))
        {
            totalTicks += TimeSpan.FromMilliseconds(double.Parse(currentNumber)).Ticks;
        }

        return TimeSpan.FromTicks(totalTicks);
    }

    /// <summary>
    /// Try to parse a duration string, returning false on failure
    /// </summary>
    public static bool TryParse(string duration, out TimeSpan result)
    {
        try
        {
            result = Parse(duration);
            return true;
        }
        catch
        {
            result = TimeSpan.Zero;
            return false;
        }
    }
}
