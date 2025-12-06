namespace NatsWebSocketBridge.Gateway.Services;

using NatsWebSocketBridge.Gateway.Configuration;

/// <summary>
/// Result of a JetStream publish operation
/// </summary>
public class JetStreamPublishResult
{
    /// <summary>
    /// Whether the publish was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The stream the message was published to
    /// </summary>
    public string? Stream { get; init; }

    /// <summary>
    /// The sequence number assigned to the message
    /// </summary>
    public ulong Sequence { get; init; }

    /// <summary>
    /// Whether the message was a duplicate
    /// </summary>
    public bool Duplicate { get; init; }

    /// <summary>
    /// Error message if publish failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Number of retries attempted
    /// </summary>
    public int RetryCount { get; init; }
}

/// <summary>
/// A message received from JetStream
/// </summary>
public class JetStreamMessage
{
    /// <summary>
    /// The subject the message was published to
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// The message payload
    /// </summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Message headers
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>
    /// The stream sequence number
    /// </summary>
    public ulong Sequence { get; init; }

    /// <summary>
    /// The consumer sequence number
    /// </summary>
    public ulong ConsumerSequence { get; init; }

    /// <summary>
    /// When the message was published
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Number of times this message has been delivered
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// Whether this is a redelivery
    /// </summary>
    public bool IsRedelivered => DeliveryCount > 1;

    /// <summary>
    /// The stream name
    /// </summary>
    public string Stream { get; init; } = string.Empty;

    /// <summary>
    /// The consumer name
    /// </summary>
    public string Consumer { get; init; } = string.Empty;

    /// <summary>
    /// Internal context for acknowledging this message
    /// </summary>
    internal object? AckContext { get; init; }
}

/// <summary>
/// Information about a JetStream consumer
/// </summary>
public class ConsumerInfo
{
    /// <summary>
    /// Consumer name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Stream name
    /// </summary>
    public string StreamName { get; init; } = string.Empty;

    /// <summary>
    /// Number of pending messages
    /// </summary>
    public long NumPending { get; init; }

    /// <summary>
    /// Number of messages waiting to be acknowledged
    /// </summary>
    public long NumAckPending { get; init; }

    /// <summary>
    /// Number of messages redelivered
    /// </summary>
    public long NumRedelivered { get; init; }

    /// <summary>
    /// Last delivered sequence
    /// </summary>
    public ulong Delivered { get; init; }

    /// <summary>
    /// Whether the consumer is durable
    /// </summary>
    public bool IsDurable { get; init; }

    /// <summary>
    /// Consumer creation time
    /// </summary>
    public DateTime Created { get; init; }
}

/// <summary>
/// Information about a JetStream stream
/// </summary>
public class StreamInfo
{
    /// <summary>
    /// Stream name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Subjects captured by this stream
    /// </summary>
    public List<string> Subjects { get; init; } = new();

    /// <summary>
    /// Number of messages in the stream
    /// </summary>
    public long Messages { get; init; }

    /// <summary>
    /// Total bytes in the stream
    /// </summary>
    public long Bytes { get; init; }

    /// <summary>
    /// Number of consumers
    /// </summary>
    public int ConsumerCount { get; init; }

    /// <summary>
    /// First sequence in the stream
    /// </summary>
    public ulong FirstSequence { get; init; }

    /// <summary>
    /// Last sequence in the stream
    /// </summary>
    public ulong LastSequence { get; init; }

    /// <summary>
    /// Stream creation time
    /// </summary>
    public DateTime Created { get; init; }
}

/// <summary>
/// Subscription handle for JetStream subscriptions
/// </summary>
public class JetStreamSubscription
{
    /// <summary>
    /// Unique subscription ID
    /// </summary>
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>
    /// Consumer name
    /// </summary>
    public string ConsumerName { get; init; } = string.Empty;

    /// <summary>
    /// Stream name
    /// </summary>
    public string StreamName { get; init; } = string.Empty;

    /// <summary>
    /// Subject filter
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Whether the subscription is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last acknowledged sequence
    /// </summary>
    public ulong LastAckedSequence { get; set; }

    /// <summary>
    /// Device ID if this is a device subscription
    /// </summary>
    public string? DeviceId { get; init; }
}

/// <summary>
/// Interface for JetStream-based NATS operations
/// </summary>
public interface IJetStreamNatsService : IAsyncDisposable
{
    /// <summary>
    /// Whether the service is connected and JetStream is available
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Whether JetStream is enabled and available
    /// </summary>
    bool IsJetStreamAvailable { get; }

    #region Initialization

    /// <summary>
    /// Initialize the NATS connection and JetStream context
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Stream Management

    /// <summary>
    /// Ensure a stream exists, creating it if necessary
    /// </summary>
    Task<StreamInfo> EnsureStreamExistsAsync(StreamConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about a stream
    /// </summary>
    Task<StreamInfo?> GetStreamInfoAsync(string streamName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a stream
    /// </summary>
    Task<bool> DeleteStreamAsync(string streamName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge all messages from a stream
    /// </summary>
    Task<long> PurgeStreamAsync(string streamName, string? filterSubject = null, CancellationToken cancellationToken = default);

    #endregion

    #region Publishing

    /// <summary>
    /// Publish a message to JetStream with retry and acknowledgement
    /// </summary>
    Task<JetStreamPublishResult> PublishAsync(
        string subject,
        byte[] data,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish a message to JetStream with custom retry policy
    /// </summary>
    Task<JetStreamPublishResult> PublishAsync(
        string subject,
        byte[] data,
        RetryPolicyConfiguration retryPolicy,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Consumer Management

    /// <summary>
    /// Create a durable consumer
    /// </summary>
    Task<ConsumerInfo> CreateConsumerAsync(ConsumerConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or create a consumer, reusing existing if configuration matches
    /// </summary>
    Task<ConsumerInfo> GetOrCreateConsumerAsync(ConsumerConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about a consumer
    /// </summary>
    Task<ConsumerInfo?> GetConsumerInfoAsync(string streamName, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a consumer
    /// </summary>
    Task<bool> DeleteConsumerAsync(string streamName, string consumerName, CancellationToken cancellationToken = default);

    #endregion

    #region Message Consumption

    /// <summary>
    /// Fetch messages from a pull consumer
    /// </summary>
    Task<IReadOnlyList<JetStreamMessage>> FetchMessagesAsync(
        string streamName,
        string consumerName,
        int batchSize = 100,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to messages using a consumer with a handler
    /// </summary>
    Task<JetStreamSubscription> SubscribeAsync(
        string streamName,
        string consumerName,
        Func<JetStreamMessage, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to messages with replay options (for device reconnection)
    /// </summary>
    Task<JetStreamSubscription> SubscribeWithReplayAsync(
        string streamName,
        string subject,
        string consumerNamePrefix,
        ReplayOptions replayOptions,
        Func<JetStreamMessage, Task> handler,
        string? deviceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe and optionally delete the consumer
    /// </summary>
    Task UnsubscribeAsync(string subscriptionId, bool deleteConsumer = false, CancellationToken cancellationToken = default);

    #endregion

    #region Message Acknowledgement

    /// <summary>
    /// Acknowledge a message
    /// </summary>
    Task AckMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Negative acknowledge a message (will be redelivered)
    /// </summary>
    Task NakMessageAsync(JetStreamMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicate message is in progress (extends ack deadline)
    /// </summary>
    Task InProgressAsync(JetStreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminate message (will not be redelivered)
    /// </summary>
    Task TerminateMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default);

    #endregion

    #region Device Subscriptions

    /// <summary>
    /// Subscribe a device to a subject pattern, using shared or dedicated consumer based on configuration
    /// </summary>
    Task<JetStreamSubscription> SubscribeDeviceAsync(
        string deviceId,
        string subject,
        Func<JetStreamMessage, Task> handler,
        ReplayOptions? replayOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe a device
    /// </summary>
    Task UnsubscribeDeviceAsync(string deviceId, string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all subscriptions for a device
    /// </summary>
    IReadOnlyList<JetStreamSubscription> GetDeviceSubscriptions(string deviceId);

    #endregion

    #region Observability

    /// <summary>
    /// Get consumer lag (pending messages)
    /// </summary>
    Task<long> GetConsumerLagAsync(string streamName, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all stream information
    /// </summary>
    Task<IReadOnlyList<StreamInfo>> GetAllStreamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all consumer information for a stream
    /// </summary>
    Task<IReadOnlyList<ConsumerInfo>> GetAllConsumersAsync(string streamName, CancellationToken cancellationToken = default);

    #endregion
}
