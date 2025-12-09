using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;
using NATS.Client.JetStream;
using NatsWebSocketBridge.Gateway.Configuration;

// Type aliases to resolve ambiguity between NATS.Client.JetStream and local Configuration types
using NatsStreamConfig = NATS.Client.JetStream.StreamConfiguration;
using NatsConsumerConfig = NATS.Client.JetStream.ConsumerConfiguration;
using NatsStreamInfo = NATS.Client.JetStream.StreamInfo;
using NatsConsumerInfo = NATS.Client.JetStream.ConsumerInfo;
using NatsAckPolicy = NATS.Client.JetStream.AckPolicy;
using NatsReplayPolicy = NATS.Client.JetStream.ReplayPolicy;
using NatsDeliverPolicy = NATS.Client.JetStream.DeliverPolicy;
using GatewayStreamConfig = NatsWebSocketBridge.Gateway.Configuration.StreamConfiguration;
using GatewayConsumerConfig = NatsWebSocketBridge.Gateway.Configuration.ConsumerConfiguration;
using GatewayJetStreamOptions = NatsWebSocketBridge.Gateway.Configuration.JetStreamOptions;
using NatsClientOptions = NATS.Client.Options;
using Duration = NATS.Client.Internals.Duration;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Production-grade JetStream NATS service implementation (NATS.Client v1 API)
/// </summary>
public class JetStreamNatsService : IJetStreamNatsService
{
    private readonly ILogger<JetStreamNatsService> _logger;
    private readonly NatsOptions _natsOptions;
    private readonly GatewayJetStreamOptions _jetStreamOptions;

    private IConnection? _connection;
    private IJetStream? _jetStream;
    private IJetStreamManagement? _jetStreamManagement;

    private readonly ConcurrentDictionary<string, NatsStreamInfo> _streams = new();
    private readonly ConcurrentDictionary<string, NatsConsumerInfo> _consumers = new();
    private readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions = new();
    private readonly ConcurrentDictionary<string, List<JetStreamSubscription>> _deviceSubscriptions = new();
    private readonly ConcurrentDictionary<string, SharedConsumerState> _sharedConsumers = new();

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly Random _jitterRandom = new();

    private bool _disposed;

    public bool IsConnected => _connection?.State == ConnState.CONNECTED;
    public bool IsJetStreamAvailable => _jetStream != null;

    public JetStreamNatsService(
        ILogger<JetStreamNatsService> logger,
        IOptions<NatsOptions> natsOptions,
        IOptions<GatewayJetStreamOptions> jetStreamOptions)
    {
        _logger = logger;
        _natsOptions = natsOptions.Value;
        _jetStreamOptions = jetStreamOptions.Value;
    }

    #region Initialization

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null)
            {
                _logger.LogDebug("NATS connection already initialized");
                return;
            }

            _logger.LogInformation("Connecting to NATS at {Url}", _natsOptions.Url);

            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _natsOptions.Url;
            opts.Name = _natsOptions.ClientName;
            opts.AllowReconnect = true;
            opts.MaxReconnect = NatsClientOptions.ReconnectForever;
            opts.ReconnectWait = 1000;

            var factory = new ConnectionFactory();
            _connection = factory.CreateConnection(opts);

            _logger.LogInformation("Connected to NATS server");

            if (_jetStreamOptions.Enabled)
            {
                await InitializeJetStreamAsync(cancellationToken);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private Task InitializeJetStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            _jetStream = _connection!.CreateJetStreamContext();
            _jetStreamManagement = _connection.CreateJetStreamManagementContext();
            _logger.LogInformation("JetStream context initialized");

            // Create configured streams
            foreach (var streamConfig in _jetStreamOptions.Streams)
            {
                try
                {
                    EnsureStreamExistsSync(streamConfig);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create stream {StreamName}", streamConfig.Name);
                }
            }

            // Create configured consumers
            foreach (var consumerConfig in _jetStreamOptions.Consumers)
            {
                try
                {
                    GetOrCreateConsumerSync(consumerConfig);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create consumer {ConsumerName} on stream {StreamName}",
                        consumerConfig.Name, consumerConfig.StreamName);
                }
            }

            _logger.LogInformation("JetStream initialization complete. Streams: {StreamCount}, Consumers: {ConsumerCount}",
                _streams.Count, _consumers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JetStream not available, service will operate in degraded mode");
            _jetStream = null;
            _jetStreamManagement = null;
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Stream Management

    public Task<StreamInfo> EnsureStreamExistsAsync(GatewayStreamConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(EnsureStreamExistsSync(config));
    }

    private StreamInfo EnsureStreamExistsSync(GatewayStreamConfig config)
    {
        EnsureJetStreamAvailable();

        var streamKey = config.Name;

        try
        {
            // Try to get existing stream
            var existingStream = _jetStreamManagement!.GetStreamInfo(config.Name);
            _streams[streamKey] = existingStream;

            _logger.LogInformation("Stream {StreamName} already exists with {MessageCount} messages",
                config.Name, existingStream.State.Messages);

            return MapStreamInfo(existingStream);
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10059) // Stream not found
        {
            // Stream doesn't exist, create it
            _logger.LogInformation("Creating stream {StreamName} with subjects: {Subjects}",
                config.Name, string.Join(", ", config.Subjects));

            var natsStreamConfig = BuildStreamConfig(config);
            var newStream = _jetStreamManagement!.AddStream(natsStreamConfig);
            _streams[streamKey] = newStream;

            _logger.LogInformation("Stream {StreamName} created successfully", config.Name);

            return MapStreamInfo(newStream);
        }
    }

    public Task<StreamInfo?> GetStreamInfoAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            var stream = _jetStreamManagement!.GetStreamInfo(streamName);
            return Task.FromResult<StreamInfo?>(MapStreamInfo(stream));
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10059)
        {
            return Task.FromResult<StreamInfo?>(null);
        }
    }

    public Task<bool> DeleteStreamAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            _jetStreamManagement!.DeleteStream(streamName);
            _streams.TryRemove(streamName, out _);

            _logger.LogInformation("Stream {StreamName} deleted", streamName);
            return Task.FromResult(true);
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10059)
        {
            _logger.LogWarning("Stream {StreamName} not found for deletion", streamName);
            return Task.FromResult(false);
        }
    }

    public Task<long> PurgeStreamAsync(string streamName, string? filterSubject = null, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        PurgeResponse response;
        if (!string.IsNullOrEmpty(filterSubject))
        {
            var options = PurgeOptions.Builder().WithSubject(filterSubject).Build();
            response = _jetStreamManagement!.PurgeStream(streamName, options);
        }
        else
        {
            response = _jetStreamManagement!.PurgeStream(streamName);
        }

        _logger.LogInformation("Purged {PurgedCount} messages from stream {StreamName}", response.Purged, streamName);

        return Task.FromResult((long)response.Purged);
    }

    private NatsStreamConfig BuildStreamConfig(GatewayStreamConfig config)
    {
        var subjects = config.Subjects.Count > 0 ? config.Subjects : new List<string> { $"{config.Name.ToLower()}.>" };

        var builder = NatsStreamConfig.Builder()
            .WithName(config.Name)
            .WithSubjects(subjects)
            .WithDescription(config.Description)
            .WithRetentionPolicy(config.Retention switch
            {
                StreamRetentionPolicy.Interest => RetentionPolicy.Interest,
                StreamRetentionPolicy.WorkQueue => RetentionPolicy.WorkQueue,
                _ => RetentionPolicy.Limits
            })
            .WithStorageType(config.Storage switch
            {
                StreamStorageType.File => StorageType.File,
                _ => StorageType.Memory
            })
            .WithMaxAge(Duration.OfMillis((long)DurationParser.Parse(config.MaxAge).TotalMilliseconds))
            .WithMaxMessages(config.MaxMessages)
            .WithMaximumMessageSize(config.MaxMessageSize)
            .WithReplicas(config.Replicas)
            .WithDiscardPolicy(config.Discard switch
            {
                StreamDiscardPolicy.New => DiscardPolicy.New,
                _ => DiscardPolicy.Old
            })
            .WithAllowDirect(config.AllowDirect)
            .WithAllowRollup(config.AllowRollup)
            .WithDenyDelete(config.DenyDelete)
            .WithDenyPurge(config.DenyPurge);

        if (config.MaxBytes > 0)
        {
            builder.WithMaxBytes(config.MaxBytes);
        }

        return builder.Build();
    }

    private static StreamInfo MapStreamInfo(NatsStreamInfo info)
    {
        return new StreamInfo
        {
            Name = info.Config.Name ?? string.Empty,
            Subjects = info.Config.Subjects?.ToList() ?? new List<string>(),
            Messages = (long)info.State.Messages,
            Bytes = (long)info.State.Bytes,
            ConsumerCount = (int)info.State.ConsumerCount,
            FirstSequence = info.State.FirstSeq,
            LastSequence = info.State.LastSeq,
            Created = info.Created
        };
    }

    #endregion

    #region Publishing

    public Task<JetStreamPublishResult> PublishAsync(
        string subject,
        byte[] data,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync(subject, data, _jetStreamOptions.PublishRetryPolicy, headers, messageId, cancellationToken);
    }

    public async Task<JetStreamPublishResult> PublishAsync(
        string subject,
        byte[] data,
        RetryPolicyConfiguration retryPolicy,
        Dictionary<string, string>? headers = null,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var retryCount = 0;
        var delay = DurationParser.Parse(retryPolicy.InitialDelay);
        var maxDelay = DurationParser.Parse(retryPolicy.MaxDelay);

        while (true)
        {
            try
            {
                var optsBuilder = PublishOptions.Builder();
                if (!string.IsNullOrEmpty(messageId))
                {
                    optsBuilder.WithMessageId(messageId);
                }
                var opts = optsBuilder.Build();

                MsgHeader? natsHeaders = null;
                if (headers != null && headers.Count > 0)
                {
                    natsHeaders = new MsgHeader();
                    foreach (var (key, value) in headers)
                    {
                        natsHeaders.Add(key, value);
                    }
                }

                var msg = new Msg(subject, natsHeaders, data);
                var ack = _jetStream!.Publish(msg, opts);

                _logger.LogDebug("Published message to {Subject}. Stream: {Stream}, Seq: {Sequence}, Duplicate: {Duplicate}",
                    subject, ack.Stream, ack.Seq, ack.Duplicate);

                return new JetStreamPublishResult
                {
                    Success = true,
                    Stream = ack.Stream,
                    Sequence = ack.Seq,
                    Duplicate = ack.Duplicate,
                    RetryCount = retryCount
                };
            }
            catch (NATSJetStreamException ex) when (IsTransientError(ex) && retryCount < retryPolicy.MaxRetries)
            {
                retryCount++;

                var jitter = retryPolicy.AddJitter
                    ? TimeSpan.FromMilliseconds(_jitterRandom.Next(0, (int)delay.TotalMilliseconds / 4))
                    : TimeSpan.Zero;

                var actualDelay = delay + jitter;

                _logger.LogWarning(ex, "Transient error publishing to {Subject}, retrying in {Delay}ms (attempt {Attempt}/{MaxAttempts})",
                    subject, actualDelay.TotalMilliseconds, retryCount, retryPolicy.MaxRetries);

                await Task.Delay(actualDelay, cancellationToken);

                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * retryPolicy.BackoffMultiplier, maxDelay.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message to {Subject} after {RetryCount} retries", subject, retryCount);

                return new JetStreamPublishResult
                {
                    Success = false,
                    Error = ex.Message,
                    RetryCount = retryCount
                };
            }
        }
    }

    private static bool IsTransientError(NATSJetStreamException ex)
    {
        // 503 = No responders, 504 = Timeout
        return ex.ErrorCode is 503 or 504;
    }

    #endregion

    #region Consumer Management

    public Task<ConsumerInfo> CreateConsumerAsync(GatewayConsumerConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateConsumerSync(config));
    }

    private ConsumerInfo CreateConsumerSync(GatewayConsumerConfig config)
    {
        EnsureJetStreamAvailable();

        var natsConsumerConfig = BuildConsumerConfig(config);
        var consumer = _jetStreamManagement!.AddOrUpdateConsumer(config.StreamName, natsConsumerConfig);
        var consumerKey = $"{config.StreamName}:{config.DurableName}";
        _consumers[consumerKey] = consumer;

        _logger.LogInformation("Consumer {ConsumerName} created on stream {StreamName}",
            config.DurableName, config.StreamName);

        return MapConsumerInfo(consumer);
    }

    public Task<ConsumerInfo> GetOrCreateConsumerAsync(GatewayConsumerConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetOrCreateConsumerSync(config));
    }

    private ConsumerInfo GetOrCreateConsumerSync(GatewayConsumerConfig config)
    {
        EnsureJetStreamAvailable();

        var consumerKey = $"{config.StreamName}:{config.DurableName}";

        // Check cache first
        if (_consumers.TryGetValue(consumerKey, out var cachedConsumer))
        {
            return MapConsumerInfo(cachedConsumer);
        }

        try
        {
            // Try to get existing consumer
            var existingConsumer = _jetStreamManagement!.GetConsumerInfo(config.StreamName, config.DurableName);
            _consumers[consumerKey] = existingConsumer;

            _logger.LogDebug("Using existing consumer {ConsumerName} on stream {StreamName}",
                config.DurableName, config.StreamName);

            return MapConsumerInfo(existingConsumer);
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10014) // Consumer not found
        {
            // Consumer doesn't exist, create it
            return CreateConsumerSync(config);
        }
    }

    public Task<ConsumerInfo?> GetConsumerInfoAsync(string streamName, string consumerName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            var consumer = _jetStreamManagement!.GetConsumerInfo(streamName, consumerName);
            return Task.FromResult<ConsumerInfo?>(MapConsumerInfo(consumer));
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10014)
        {
            return Task.FromResult<ConsumerInfo?>(null);
        }
    }

    public Task<bool> DeleteConsumerAsync(string streamName, string consumerName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            _jetStreamManagement!.DeleteConsumer(streamName, consumerName);

            var consumerKey = $"{streamName}:{consumerName}";
            _consumers.TryRemove(consumerKey, out _);

            _logger.LogInformation("Consumer {ConsumerName} deleted from stream {StreamName}", consumerName, streamName);
            return Task.FromResult(true);
        }
        catch (NATSJetStreamException ex) when (ex.ErrorCode == 10014)
        {
            _logger.LogWarning("Consumer {ConsumerName} not found on stream {StreamName}", consumerName, streamName);
            return Task.FromResult(false);
        }
    }

    private NatsConsumerConfig BuildConsumerConfig(GatewayConsumerConfig config)
    {
        var builder = NatsConsumerConfig.Builder()
            .WithDurable(config.DurableName)
            .WithDescription(config.Description)
            .WithAckPolicy(config.AckPolicy switch
            {
                Configuration.AckPolicy.None => NatsAckPolicy.None,
                Configuration.AckPolicy.All => NatsAckPolicy.All,
                _ => NatsAckPolicy.Explicit
            })
            .WithAckWait(Duration.OfMillis((long)DurationParser.Parse(config.AckWait).TotalMilliseconds))
            .WithMaxDeliver(config.MaxDeliver)
            .WithMaxAckPending(config.MaxAckPending)
            .WithDeliverPolicy(config.DeliveryPolicy switch
            {
                Configuration.DeliveryPolicy.Last => NatsDeliverPolicy.Last,
                Configuration.DeliveryPolicy.New => NatsDeliverPolicy.New,
                Configuration.DeliveryPolicy.ByStartSequence => NatsDeliverPolicy.ByStartSequence,
                Configuration.DeliveryPolicy.ByStartTime => NatsDeliverPolicy.ByStartTime,
                Configuration.DeliveryPolicy.LastPerSubject => NatsDeliverPolicy.LastPerSubject,
                _ => NatsDeliverPolicy.All
            })
            .WithReplayPolicy(config.ReplayPolicy switch
            {
                Configuration.ReplayPolicy.Original => NatsReplayPolicy.Original,
                _ => NatsReplayPolicy.Instant
            });

        if (!string.IsNullOrEmpty(config.FilterSubject))
        {
            builder.WithFilterSubject(config.FilterSubject);
        }

        // Push consumer settings
        if (config.Type == ConsumerType.Push && !string.IsNullOrEmpty(config.DeliverSubject))
        {
            builder.WithDeliverSubject(config.DeliverSubject);
            if (!string.IsNullOrEmpty(config.DeliverGroup))
            {
                builder.WithDeliverGroup(config.DeliverGroup);
            }
            builder.WithFlowControl(Duration.OfMillis((long)DurationParser.Parse(config.IdleHeartbeat).TotalMilliseconds));
        }

        return builder.Build();
    }

    private static ConsumerInfo MapConsumerInfo(NatsConsumerInfo info)
    {
        return new ConsumerInfo
        {
            Name = info.Name,
            StreamName = info.Stream,
            NumPending = (long)info.NumPending,
            NumAckPending = info.NumAckPending,
            NumRedelivered = (long)info.NumRedelivered,
            Delivered = info.Delivered.StreamSeq,
            IsDurable = !string.IsNullOrEmpty(info.ConsumerConfiguration.Durable),
            Created = info.Created
        };
    }

    #endregion

    #region Message Consumption

    public Task<IReadOnlyList<JetStreamMessage>> FetchMessagesAsync(
        string streamName,
        string consumerName,
        int batchSize = 100,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();
        return Task.FromResult(FetchMessagesSync(streamName, consumerName, batchSize, timeout));
    }

    private IReadOnlyList<JetStreamMessage> FetchMessagesSync(
        string streamName,
        string consumerName,
        int batchSize,
        TimeSpan? timeout)
    {

        var fetchTimeout = timeout ?? DurationParser.Parse(_jetStreamOptions.DefaultConsumerOptions.FetchTimeout);
        var messages = new List<JetStreamMessage>();

        try
        {
            var pullOpts = PullSubscribeOptions.Builder()
                .WithDurable(consumerName)
                .WithStream(streamName)
                .Build();

            using var subscription = _jetStream!.PullSubscribe(null, pullOpts);

            var fetched = subscription.Fetch(batchSize, (int)fetchTimeout.TotalMilliseconds);

            foreach (var msg in fetched)
            {
                messages.Add(MapNatsMessage(msg, streamName, consumerName));
            }
        }
        catch (NATSTimeoutException)
        {
            // Timeout is expected when no messages are available
            _logger.LogDebug("Fetch timeout for consumer {ConsumerName}, received {MessageCount} messages",
                consumerName, messages.Count);
        }

        if (messages.Count > 0)
        {
            _logger.LogDebug("Fetched {MessageCount} messages from consumer {ConsumerName}",
                messages.Count, consumerName);
        }

        return messages;
    }

    public Task<JetStreamSubscription> SubscribeAsync(
        string streamName,
        string consumerName,
        Func<JetStreamMessage, Task> handler,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var subscriptionId = Guid.NewGuid().ToString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pullOpts = PullSubscribeOptions.Builder()
            .WithDurable(consumerName)
            .WithStream(streamName)
            .Build();

        var natsSubscription = _jetStream!.PullSubscribe(null, pullOpts);

        // Get filter subject from consumer config if available
        string filterSubject = "*";
        try
        {
            var consumerInfo = _jetStreamManagement!.GetConsumerInfo(streamName, consumerName);
            filterSubject = consumerInfo.ConsumerConfiguration.FilterSubject ?? "*";
        }
        catch
        {
            // Ignore - use default
        }

        var subscription = new JetStreamSubscription
        {
            SubscriptionId = subscriptionId,
            ConsumerName = consumerName,
            StreamName = streamName,
            Subject = filterSubject,
            IsActive = true
        };

        var state = new SubscriptionState
        {
            Subscription = subscription,
            CancellationTokenSource = cts,
            Handler = handler,
            NatsSubscription = natsSubscription
        };

        _subscriptions[subscriptionId] = state;

        // Start consuming in background
        _ = Task.Run(async () => await ConsumeMessagesAsync(state, streamName, consumerName), cts.Token);

        _logger.LogInformation("Started subscription {SubscriptionId} for consumer {ConsumerName} on stream {StreamName}",
            subscriptionId, consumerName, streamName);

        return Task.FromResult(subscription);
    }

    public async Task<JetStreamSubscription> SubscribeWithReplayAsync(
        string streamName,
        string subject,
        string consumerNamePrefix,
        ReplayOptions replayOptions,
        Func<JetStreamMessage, Task> handler,
        string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        // Create a unique consumer for this subscription with replay options
        var consumerName = $"{consumerNamePrefix}-{Guid.NewGuid():N}".Substring(0, Math.Min(48, $"{consumerNamePrefix}".Length + 33));

        var consumerConfig = new GatewayConsumerConfig
        {
            Name = consumerName,
            StreamName = streamName,
            DurableName = consumerName,
            FilterSubject = subject,
            AckPolicy = Configuration.AckPolicy.Explicit,
            AckWait = _jetStreamOptions.DefaultConsumerOptions.AckWait,
            MaxDeliver = _jetStreamOptions.DefaultConsumerOptions.MaxDeliver,
            MaxAckPending = _jetStreamOptions.DefaultConsumerOptions.MaxAckPending,
            DeliveryPolicy = MapReplayModeToDeliveryPolicy(replayOptions.Mode),
            ReplayPolicy = Configuration.ReplayPolicy.Instant,
            Type = ConsumerType.Pull
        };

        await CreateConsumerAsync(consumerConfig, cancellationToken);

        var subscription = await SubscribeAsync(streamName, consumerName, handler, cancellationToken);

        // Create updated subscription with device ID
        var updatedSubscription = new JetStreamSubscription
        {
            SubscriptionId = subscription.SubscriptionId,
            ConsumerName = subscription.ConsumerName,
            StreamName = subscription.StreamName,
            Subject = subscription.Subject,
            IsActive = subscription.IsActive,
            LastAckedSequence = subscription.LastAckedSequence,
            DeviceId = deviceId
        };

        return updatedSubscription;
    }

    private static Configuration.DeliveryPolicy MapReplayModeToDeliveryPolicy(ReplayMode mode)
    {
        return mode switch
        {
            ReplayMode.All => Configuration.DeliveryPolicy.All,
            ReplayMode.Last => Configuration.DeliveryPolicy.Last,
            ReplayMode.FromSequence => Configuration.DeliveryPolicy.ByStartSequence,
            ReplayMode.FromTime => Configuration.DeliveryPolicy.ByStartTime,
            ReplayMode.ResumeFromLastAck => Configuration.DeliveryPolicy.All,
            _ => Configuration.DeliveryPolicy.New
        };
    }

    private async Task ConsumeMessagesAsync(
        SubscriptionState state,
        string streamName,
        string consumerName)
    {
        var batchSize = _jetStreamOptions.DefaultConsumerOptions.DefaultBatchSize;
        var fetchTimeout = (int)DurationParser.Parse(_jetStreamOptions.DefaultConsumerOptions.FetchTimeout).TotalMilliseconds;

        try
        {
            while (!state.CancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var messages = state.NatsSubscription!.Fetch(batchSize, fetchTimeout);

                    foreach (var msg in messages)
                    {
                        if (state.CancellationTokenSource.IsCancellationRequested)
                            break;

                        var jsMessage = MapNatsMessage(msg, streamName, consumerName);

                        if (jsMessage.IsRedelivered)
                        {
                            _logger.LogDebug("Processing redelivered message. Subject: {Subject}, Sequence: {Sequence}, DeliveryCount: {DeliveryCount}",
                                jsMessage.Subject, jsMessage.Sequence, jsMessage.DeliveryCount);
                        }

                        try
                        {
                            await state.Handler(jsMessage);
                            msg.Ack();
                            state.Subscription.LastAckedSequence = jsMessage.Sequence;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message from {Subject}, will be redelivered", jsMessage.Subject);
                            msg.Nak();
                        }
                    }
                }
                catch (NATSTimeoutException)
                {
                    // Expected when no messages are available
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Subscription {SubscriptionId} cancelled", state.Subscription.SubscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in subscription {SubscriptionId}", state.Subscription.SubscriptionId);
            state.Subscription.IsActive = false;
        }
    }

    public Task UnsubscribeAsync(string subscriptionId, bool deleteConsumer = false, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var state))
        {
            state.CancellationTokenSource.Cancel();
            state.NatsSubscription?.Unsubscribe();
            state.NatsSubscription?.Dispose();
            state.CancellationTokenSource.Dispose();
            state.Subscription.IsActive = false;

            _logger.LogInformation("Unsubscribed {SubscriptionId}", subscriptionId);

            if (deleteConsumer)
            {
                return DeleteConsumerAsync(state.Subscription.StreamName, state.Subscription.ConsumerName, cancellationToken);
            }
        }

        return Task.CompletedTask;
    }

    private JetStreamMessage MapNatsMessage(Msg msg, string streamName, string consumerName)
    {
        var headers = new Dictionary<string, string>();
        if (msg.Header != null)
        {
            foreach (string key in msg.Header.Keys)
            {
                headers[key] = string.Join(",", msg.Header.GetValues(key));
            }
        }

        var meta = msg.MetaData;

        return new JetStreamMessage
        {
            Subject = msg.Subject,
            Data = msg.Data ?? Array.Empty<byte>(),
            Headers = headers,
            Sequence = meta?.StreamSequence ?? 0,
            ConsumerSequence = meta?.ConsumerSequence ?? 0,
            Timestamp = meta?.Timestamp ?? DateTime.UtcNow,
            DeliveryCount = (int)(meta?.NumDelivered ?? 1),
            Stream = streamName,
            Consumer = consumerName,
            AckContext = msg
        };
    }

    #endregion

    #region Message Acknowledgement

    public Task AckMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is Msg msg)
        {
            msg.Ack();
            _logger.LogDebug("Acknowledged message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
        return Task.CompletedTask;
    }

    public Task NakMessageAsync(JetStreamMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is Msg msg)
        {
            if (delay.HasValue)
            {
                msg.NakWithDelay(Duration.OfMillis((long)delay.Value.TotalMilliseconds));
            }
            else
            {
                msg.Nak();
            }
            _logger.LogDebug("NAK'd message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
        return Task.CompletedTask;
    }

    public Task InProgressAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is Msg msg)
        {
            msg.InProgress();
            _logger.LogDebug("Extended ack deadline. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
        return Task.CompletedTask;
    }

    public Task TerminateMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is Msg msg)
        {
            msg.Term();
            _logger.LogDebug("Terminated message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
        return Task.CompletedTask;
    }

    #endregion

    #region Device Subscriptions

    public async Task<JetStreamSubscription> SubscribeDeviceAsync(
        string deviceId,
        string subject,
        Func<JetStreamMessage, Task> handler,
        ReplayOptions? replayOptions = null,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        // Find the stream that handles this subject
        var streamName = FindStreamForSubject(subject);
        if (streamName == null)
        {
            throw new InvalidOperationException($"No stream found for subject pattern: {subject}");
        }

        // Check if we can use a shared consumer
        var sharedConsumerKey = $"{streamName}:{subject}";

        JetStreamSubscription subscription;

        if (_sharedConsumers.TryGetValue(sharedConsumerKey, out var sharedState))
        {
            // Add device to shared consumer's fanout
            subscription = new JetStreamSubscription
            {
                SubscriptionId = Guid.NewGuid().ToString(),
                ConsumerName = sharedState.ConsumerName,
                StreamName = streamName,
                Subject = subject,
                IsActive = true,
                DeviceId = deviceId
            };

            sharedState.AddHandler(deviceId, handler);

            _logger.LogInformation("Device {DeviceId} joined shared consumer {ConsumerName} for subject {Subject}",
                deviceId, sharedState.ConsumerName, subject);
        }
        else
        {
            // Create a new consumer for this device/subject combination
            var consumerName = $"device-{deviceId}-{Guid.NewGuid():N}".Substring(0, Math.Min(48, $"device-{deviceId}".Length + 33));

            subscription = await SubscribeWithReplayAsync(
                streamName,
                subject,
                consumerName,
                replayOptions ?? new ReplayOptions { Mode = ReplayMode.New },
                handler,
                deviceId,
                cancellationToken);

            _logger.LogInformation("Created dedicated consumer {ConsumerName} for device {DeviceId} on subject {Subject}",
                consumerName, deviceId, subject);
        }

        // Track device subscriptions
        _deviceSubscriptions.AddOrUpdate(
            deviceId,
            _ => new List<JetStreamSubscription> { subscription },
            (_, list) => { list.Add(subscription); return list; });

        return subscription;
    }

    public async Task UnsubscribeDeviceAsync(string deviceId, string subscriptionId, CancellationToken cancellationToken = default)
    {
        if (_deviceSubscriptions.TryGetValue(deviceId, out var subscriptions))
        {
            var subscription = subscriptions.FirstOrDefault(s => s.SubscriptionId == subscriptionId);
            if (subscription != null)
            {
                subscriptions.Remove(subscription);

                // Check if this was a shared consumer subscription
                var sharedConsumerKey = $"{subscription.StreamName}:{subscription.Subject}";
                if (_sharedConsumers.TryGetValue(sharedConsumerKey, out var sharedState))
                {
                    sharedState.RemoveHandler(deviceId);
                    _logger.LogInformation("Device {DeviceId} left shared consumer for subject {Subject}", deviceId, subscription.Subject);
                }
                else
                {
                    // Dedicated consumer - unsubscribe and delete
                    await UnsubscribeAsync(subscriptionId, deleteConsumer: true, cancellationToken);
                }
            }

            if (subscriptions.Count == 0)
            {
                _deviceSubscriptions.TryRemove(deviceId, out _);
            }
        }
    }

    public IReadOnlyList<JetStreamSubscription> GetDeviceSubscriptions(string deviceId)
    {
        return _deviceSubscriptions.TryGetValue(deviceId, out var subscriptions)
            ? subscriptions.ToList()
            : Array.Empty<JetStreamSubscription>();
    }

    private string? FindStreamForSubject(string subject)
    {
        // Check configured streams first
        foreach (var streamConfig in _jetStreamOptions.Streams)
        {
            foreach (var pattern in streamConfig.Subjects)
            {
                if (SubjectMatchesPattern(subject, pattern))
                {
                    return streamConfig.Name;
                }
            }
        }

        // Fall back to checking cached streams
        foreach (var (name, stream) in _streams)
        {
            var info = stream;
            if (info.Config.Subjects?.Any(pattern => SubjectMatchesPattern(subject, pattern)) == true)
            {
                return name;
            }
        }

        return null;
    }

    private static bool SubjectMatchesPattern(string subject, string pattern)
    {
        if (pattern == subject) return true;
        if (pattern.EndsWith(">")) return subject.StartsWith(pattern[..^1]);
        if (pattern.Contains("*"))
        {
            var patternParts = pattern.Split('.');
            var subjectParts = subject.Split('.');
            if (patternParts.Length != subjectParts.Length) return false;

            for (int i = 0; i < patternParts.Length; i++)
            {
                if (patternParts[i] != "*" && patternParts[i] != subjectParts[i])
                    return false;
            }
            return true;
        }
        return false;
    }

    #endregion

    #region Observability

    public async Task<long> GetConsumerLagAsync(string streamName, string consumerName, CancellationToken cancellationToken = default)
    {
        var info = await GetConsumerInfoAsync(streamName, consumerName, cancellationToken);
        if (info == null) return -1;

        var lag = info.NumPending;

        if (lag > 0)
        {
            _logger.LogDebug("Consumer {ConsumerName} lag: {Lag} pending messages", consumerName, lag);
        }

        return lag;
    }

    public Task<IReadOnlyList<StreamInfo>> GetAllStreamsAsync(CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var streams = new List<StreamInfo>();
        var streamNames = _jetStreamManagement!.GetStreamNames();

        foreach (var name in streamNames)
        {
            try
            {
                var info = _jetStreamManagement.GetStreamInfo(name);
                streams.Add(MapStreamInfo(info));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get info for stream {StreamName}", name);
            }
        }

        return Task.FromResult<IReadOnlyList<StreamInfo>>(streams);
    }

    public Task<IReadOnlyList<ConsumerInfo>> GetAllConsumersAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var consumers = new List<ConsumerInfo>();
        var consumerNames = _jetStreamManagement!.GetConsumerNames(streamName);

        foreach (var name in consumerNames)
        {
            try
            {
                var info = _jetStreamManagement.GetConsumerInfo(streamName, name);
                consumers.Add(MapConsumerInfo(info));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get info for consumer {ConsumerName}", name);
            }
        }

        return Task.FromResult<IReadOnlyList<ConsumerInfo>>(consumers);
    }

    #endregion

    #region Helpers

    private void EnsureJetStreamAvailable()
    {
        if (_jetStream == null || _jetStreamManagement == null)
        {
            throw new InvalidOperationException("JetStream is not available. Ensure the service is initialized and JetStream is enabled.");
        }
    }

    #endregion

    #region Disposal

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        // Cancel all subscriptions
        foreach (var (_, state) in _subscriptions)
        {
            state.CancellationTokenSource.Cancel();
            state.NatsSubscription?.Unsubscribe();
            state.NatsSubscription?.Dispose();
            state.CancellationTokenSource.Dispose();
        }
        _subscriptions.Clear();

        // Clear shared consumers
        foreach (var (_, state) in _sharedConsumers)
        {
            state.CancellationTokenSource.Cancel();
            state.CancellationTokenSource.Dispose();
        }
        _sharedConsumers.Clear();

        _deviceSubscriptions.Clear();
        _consumers.Clear();
        _streams.Clear();

        _connection?.Drain();
        _connection?.Close();
        _connection?.Dispose();

        _initLock.Dispose();

        _logger.LogInformation("JetStream NATS service disposed");

        return ValueTask.CompletedTask;
    }

    #endregion

    #region Internal Types

    private class SubscriptionState
    {
        public required JetStreamSubscription Subscription { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
        public required Func<JetStreamMessage, Task> Handler { get; init; }
        public IJetStreamPullSubscription? NatsSubscription { get; init; }
    }

    private class SharedConsumerState
    {
        public required string ConsumerName { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
        private readonly ConcurrentDictionary<string, Func<JetStreamMessage, Task>> _handlers = new();

        public void AddHandler(string deviceId, Func<JetStreamMessage, Task> handler)
        {
            _handlers[deviceId] = handler;
        }

        public void RemoveHandler(string deviceId)
        {
            _handlers.TryRemove(deviceId, out _);
        }

        public IEnumerable<Func<JetStreamMessage, Task>> GetHandlers()
        {
            return _handlers.Values;
        }

        public int HandlerCount => _handlers.Count;
    }

    #endregion
}
