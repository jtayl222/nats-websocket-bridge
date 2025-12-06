using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NatsWebSocketBridge.Gateway.Configuration;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Production-grade JetStream NATS service implementation
/// </summary>
public class JetStreamNatsService : IJetStreamNatsService
{
    private readonly ILogger<JetStreamNatsService> _logger;
    private readonly NatsOptions _natsOptions;
    private readonly JetStreamOptions _jetStreamOptions;
    
    private NatsConnection? _connection;
    private NatsJSContext? _jetStream;
    
    private readonly ConcurrentDictionary<string, INatsJSStream> _streams = new();
    private readonly ConcurrentDictionary<string, INatsJSConsumer> _consumers = new();
    private readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions = new();
    private readonly ConcurrentDictionary<string, List<JetStreamSubscription>> _deviceSubscriptions = new();
    private readonly ConcurrentDictionary<string, SharedConsumerState> _sharedConsumers = new();
    
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly Random _jitterRandom = new();
    
    private bool _disposed;

    public bool IsConnected => _connection?.ConnectionState == NatsConnectionState.Open;
    public bool IsJetStreamAvailable => _jetStream != null;

    public JetStreamNatsService(
        ILogger<JetStreamNatsService> logger,
        IOptions<NatsOptions> natsOptions,
        IOptions<JetStreamOptions> jetStreamOptions)
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

            var opts = new NatsOpts
            {
                Url = _natsOptions.Url,
                Name = _natsOptions.ClientName
            };

            _connection = new NatsConnection(opts);
            await _connection.ConnectAsync();

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

    private async Task InitializeJetStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            _jetStream = new NatsJSContext(_connection!);
            _logger.LogInformation("JetStream context initialized");

            // Create configured streams
            foreach (var streamConfig in _jetStreamOptions.Streams)
            {
                try
                {
                    await EnsureStreamExistsAsync(streamConfig, cancellationToken);
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
                    await GetOrCreateConsumerAsync(consumerConfig, cancellationToken);
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
        }
    }

    #endregion

    #region Stream Management

    public async Task<StreamInfo> EnsureStreamExistsAsync(StreamConfiguration config, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var streamKey = config.Name;
        
        try
        {
            // Try to get existing stream
            var existingStream = await _jetStream!.GetStreamAsync(config.Name, cancellationToken: cancellationToken);
            _streams[streamKey] = existingStream;
            
            _logger.LogInformation("Stream {StreamName} already exists with {MessageCount} messages",
                config.Name, existingStream.Info.State.Messages);
            
            return MapStreamInfo(existingStream.Info);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream doesn't exist, create it
            _logger.LogInformation("Creating stream {StreamName} with subjects: {Subjects}",
                config.Name, string.Join(", ", config.Subjects));

            var natsStreamConfig = BuildStreamConfig(config);
            var newStream = await _jetStream!.CreateStreamAsync(natsStreamConfig, cancellationToken);
            _streams[streamKey] = newStream;

            _logger.LogInformation("Stream {StreamName} created successfully", config.Name);
            
            return MapStreamInfo(newStream.Info);
        }
    }

    public async Task<StreamInfo?> GetStreamInfoAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
            return MapStreamInfo(stream.Info);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteStreamAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            await _jetStream!.DeleteStreamAsync(streamName, cancellationToken);
            _streams.TryRemove(streamName, out _);
            
            _logger.LogInformation("Stream {StreamName} deleted", streamName);
            return true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            _logger.LogWarning("Stream {StreamName} not found for deletion", streamName);
            return false;
        }
    }

    public async Task<long> PurgeStreamAsync(string streamName, string? filterSubject = null, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
        
        var request = new StreamPurgeRequest
        {
            Filter = filterSubject
        };
        
        var response = await stream.PurgeAsync(request, cancellationToken);
        
        _logger.LogInformation("Purged {PurgedCount} messages from stream {StreamName}", response.Purged, streamName);
        
        return (long)response.Purged;
    }

    private StreamConfig BuildStreamConfig(StreamConfiguration config)
    {
        var subjects = config.Subjects.Count > 0 ? config.Subjects : new List<string> { $"{config.Name.ToLower()}.>" };
        
        return new StreamConfig(config.Name, subjects)
        {
            Description = config.Description,
            Retention = config.Retention switch
            {
                StreamRetentionPolicy.Interest => StreamConfigRetention.Interest,
                StreamRetentionPolicy.WorkQueue => StreamConfigRetention.Workqueue,
                _ => StreamConfigRetention.Limits
            },
            Storage = config.Storage switch
            {
                StreamStorageType.File => StreamConfigStorage.File,
                _ => StreamConfigStorage.Memory
            },
            MaxAge = DurationParser.Parse(config.MaxAge),
            MaxMsgs = config.MaxMessages,
            MaxBytes = config.MaxBytes,
            MaxMsgSize = config.MaxMessageSize,
            NumReplicas = config.Replicas,
            Discard = config.Discard switch
            {
                StreamDiscardPolicy.New => StreamConfigDiscard.New,
                _ => StreamConfigDiscard.Old
            },
            AllowDirect = config.AllowDirect,
            AllowRollupHdrs = config.AllowRollup,
            DenyDelete = config.DenyDelete,
            DenyPurge = config.DenyPurge
        };
    }

    private static StreamInfo MapStreamInfo(NATS.Client.JetStream.Models.StreamInfo info)
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
            Created = info.Created.UtcDateTime
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
                var opts = new NatsJSPubOpts
                {
                    MsgId = messageId
                };

                NatsHeaders? natsHeaders = null;
                if (headers != null && headers.Count > 0)
                {
                    natsHeaders = new NatsHeaders();
                    foreach (var (key, value) in headers)
                    {
                        natsHeaders.Add(key, value);
                    }
                }

                var ack = await _jetStream!.PublishAsync(subject, data, opts: opts, headers: natsHeaders, cancellationToken: cancellationToken);

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
            catch (NatsJSApiException ex) when (IsTransientError(ex) && retryCount < retryPolicy.MaxRetries)
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

    private static bool IsTransientError(NatsJSApiException ex)
    {
        // 503 = No responders, 504 = Timeout
        return ex.Error.Code is 503 or 504;
    }

    #endregion

    #region Consumer Management

    public async Task<ConsumerInfo> CreateConsumerAsync(ConsumerConfiguration config, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var stream = await GetOrCacheStreamAsync(config.StreamName, cancellationToken);
        var natsConsumerConfig = BuildConsumerConfig(config);

        var consumer = await stream.CreateOrUpdateConsumerAsync(natsConsumerConfig, cancellationToken);
        var consumerKey = $"{config.StreamName}:{config.DurableName}";
        _consumers[consumerKey] = consumer;

        _logger.LogInformation("Consumer {ConsumerName} created on stream {StreamName}",
            config.DurableName, config.StreamName);

        return MapConsumerInfo(consumer.Info);
    }

    public async Task<ConsumerInfo> GetOrCreateConsumerAsync(ConsumerConfiguration config, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var consumerKey = $"{config.StreamName}:{config.DurableName}";

        // Check cache first
        if (_consumers.TryGetValue(consumerKey, out var cachedConsumer))
        {
            return MapConsumerInfo(cachedConsumer.Info);
        }

        var stream = await GetOrCacheStreamAsync(config.StreamName, cancellationToken);

        try
        {
            // Try to get existing consumer
            var existingConsumer = await stream.GetConsumerAsync(config.DurableName, cancellationToken);
            _consumers[consumerKey] = existingConsumer;
            
            _logger.LogDebug("Using existing consumer {ConsumerName} on stream {StreamName}",
                config.DurableName, config.StreamName);
            
            return MapConsumerInfo(existingConsumer.Info);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Consumer doesn't exist, create it
            return await CreateConsumerAsync(config, cancellationToken);
        }
    }

    public async Task<ConsumerInfo?> GetConsumerInfoAsync(string streamName, string consumerName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
            var consumer = await stream.GetConsumerAsync(consumerName, cancellationToken);
            return MapConsumerInfo(consumer.Info);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteConsumerAsync(string streamName, string consumerName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        try
        {
            var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
            await stream.DeleteConsumerAsync(consumerName, cancellationToken);
            
            var consumerKey = $"{streamName}:{consumerName}";
            _consumers.TryRemove(consumerKey, out _);
            
            _logger.LogInformation("Consumer {ConsumerName} deleted from stream {StreamName}", consumerName, streamName);
            return true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            _logger.LogWarning("Consumer {ConsumerName} not found on stream {StreamName}", consumerName, streamName);
            return false;
        }
    }

    private async Task<INatsJSStream> GetOrCacheStreamAsync(string streamName, CancellationToken cancellationToken)
    {
        if (_streams.TryGetValue(streamName, out var cachedStream))
        {
            return cachedStream;
        }

        var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
        _streams[streamName] = stream;
        return stream;
    }

    private ConsumerConfig BuildConsumerConfig(ConsumerConfiguration config)
    {
        var consumerConfig = new ConsumerConfig(config.DurableName)
        {
            Description = config.Description,
            FilterSubject = string.IsNullOrEmpty(config.FilterSubject) ? null : config.FilterSubject,
            AckPolicy = config.AckPolicy switch
            {
                Configuration.AckPolicy.None => ConsumerConfigAckPolicy.None,
                Configuration.AckPolicy.All => ConsumerConfigAckPolicy.All,
                _ => ConsumerConfigAckPolicy.Explicit
            },
            AckWait = DurationParser.Parse(config.AckWait),
            MaxDeliver = config.MaxDeliver,
            MaxAckPending = config.MaxAckPending,
            DeliverPolicy = config.DeliveryPolicy switch
            {
                Configuration.DeliveryPolicy.Last => ConsumerConfigDeliverPolicy.Last,
                Configuration.DeliveryPolicy.New => ConsumerConfigDeliverPolicy.New,
                Configuration.DeliveryPolicy.ByStartSequence => ConsumerConfigDeliverPolicy.ByStartSequence,
                Configuration.DeliveryPolicy.ByStartTime => ConsumerConfigDeliverPolicy.ByStartTime,
                Configuration.DeliveryPolicy.LastPerSubject => ConsumerConfigDeliverPolicy.LastPerSubject,
                _ => ConsumerConfigDeliverPolicy.All
            },
            ReplayPolicy = config.ReplayPolicy switch
            {
                Configuration.ReplayPolicy.Original => ConsumerConfigReplayPolicy.Original,
                _ => ConsumerConfigReplayPolicy.Instant
            }
        };

        // Push consumer settings
        if (config.Type == ConsumerType.Push && !string.IsNullOrEmpty(config.DeliverSubject))
        {
            consumerConfig.DeliverSubject = config.DeliverSubject;
            consumerConfig.DeliverGroup = string.IsNullOrEmpty(config.DeliverGroup) ? null : config.DeliverGroup;
            consumerConfig.FlowControl = config.FlowControl;
            
            if (!string.IsNullOrEmpty(config.IdleHeartbeat))
            {
                consumerConfig.IdleHeartbeat = DurationParser.Parse(config.IdleHeartbeat);
            }
        }

        return consumerConfig;
    }

    private static ConsumerInfo MapConsumerInfo(NATS.Client.JetStream.Models.ConsumerInfo info)
    {
        return new ConsumerInfo
        {
            Name = info.Name,
            StreamName = info.StreamName,
            NumPending = (long)info.NumPending,
            NumAckPending = info.NumAckPending,
            NumRedelivered = (long)info.NumRedelivered,
            Delivered = info.Delivered.StreamSeq,
            IsDurable = !string.IsNullOrEmpty(info.Config.DurableName),
            Created = info.Created.UtcDateTime
        };
    }

    #endregion

    #region Message Consumption

    public async Task<IReadOnlyList<JetStreamMessage>> FetchMessagesAsync(
        string streamName,
        string consumerName,
        int batchSize = 100,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var consumerKey = $"{streamName}:{consumerName}";
        
        if (!_consumers.TryGetValue(consumerKey, out var consumer))
        {
            var stream = await GetOrCacheStreamAsync(streamName, cancellationToken);
            consumer = await stream.GetConsumerAsync(consumerName, cancellationToken);
            _consumers[consumerKey] = consumer;
        }

        var fetchTimeout = timeout ?? DurationParser.Parse(_jetStreamOptions.DefaultConsumerOptions.FetchTimeout);
        var messages = new List<JetStreamMessage>();

        try
        {
            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = batchSize, Expires = fetchTimeout },
                cancellationToken: cancellationToken))
            {
                messages.Add(MapNatsMessage(msg, streamName, consumerName));
            }
        }
        catch (NatsJSTimeoutException)
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

    public async Task<JetStreamSubscription> SubscribeAsync(
        string streamName,
        string consumerName,
        Func<JetStreamMessage, Task> handler,
        CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var subscriptionId = Guid.NewGuid().ToString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var consumerKey = $"{streamName}:{consumerName}";
        
        if (!_consumers.TryGetValue(consumerKey, out var consumer))
        {
            var stream = await GetOrCacheStreamAsync(streamName, cancellationToken);
            consumer = await stream.GetConsumerAsync(consumerName, cancellationToken);
            _consumers[consumerKey] = consumer;
        }

        var subscription = new JetStreamSubscription
        {
            SubscriptionId = subscriptionId,
            ConsumerName = consumerName,
            StreamName = streamName,
            Subject = consumer.Info.Config.FilterSubject ?? "*",
            IsActive = true
        };

        var state = new SubscriptionState
        {
            Subscription = subscription,
            CancellationTokenSource = cts,
            Handler = handler
        };

        _subscriptions[subscriptionId] = state;

        // Start consuming in background
        _ = Task.Run(async () => await ConsumeMessagesAsync(state, consumer, streamName, consumerName), cts.Token);

        _logger.LogInformation("Started subscription {SubscriptionId} for consumer {ConsumerName} on stream {StreamName}",
            subscriptionId, consumerName, streamName);

        return subscription;
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
        var consumerName = $"{consumerNamePrefix}-{Guid.NewGuid():N}";

        var consumerConfig = new ConsumerConfiguration
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
        // Update device ID on the subscription
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
        subscription = updatedSubscription;

        return subscription;
    }

    private static Configuration.DeliveryPolicy MapReplayModeToDeliveryPolicy(ReplayMode mode)
    {
        return mode switch
        {
            ReplayMode.All => Configuration.DeliveryPolicy.All,
            ReplayMode.Last => Configuration.DeliveryPolicy.Last,
            ReplayMode.FromSequence => Configuration.DeliveryPolicy.ByStartSequence,
            ReplayMode.FromTime => Configuration.DeliveryPolicy.ByStartTime,
            ReplayMode.ResumeFromLastAck => Configuration.DeliveryPolicy.All, // Will be filtered by consumer state
            _ => Configuration.DeliveryPolicy.New
        };
    }

    private async Task ConsumeMessagesAsync(
        SubscriptionState state,
        INatsJSConsumer consumer,
        string streamName,
        string consumerName)
    {
        var batchSize = _jetStreamOptions.DefaultConsumerOptions.DefaultBatchSize;
        var fetchTimeout = DurationParser.Parse(_jetStreamOptions.DefaultConsumerOptions.FetchTimeout);

        try
        {
            while (!state.CancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await foreach (var msg in consumer.FetchAsync<byte[]>(
                        new NatsJSFetchOpts { MaxMsgs = batchSize, Expires = fetchTimeout },
                        cancellationToken: state.CancellationTokenSource.Token))
                    {
                        var jsMessage = MapNatsMessage(msg, streamName, consumerName);

                        if (jsMessage.IsRedelivered)
                        {
                            _logger.LogDebug("Processing redelivered message. Subject: {Subject}, Sequence: {Sequence}, DeliveryCount: {DeliveryCount}",
                                jsMessage.Subject, jsMessage.Sequence, jsMessage.DeliveryCount);
                        }

                        try
                        {
                            await state.Handler(jsMessage);
                            await msg.AckAsync(cancellationToken: state.CancellationTokenSource.Token);
                            state.Subscription.LastAckedSequence = jsMessage.Sequence;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message from {Subject}, will be redelivered", jsMessage.Subject);
                            await msg.NakAsync(cancellationToken: state.CancellationTokenSource.Token);
                        }
                    }
                }
                catch (NatsJSTimeoutException)
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

    public async Task UnsubscribeAsync(string subscriptionId, bool deleteConsumer = false, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var state))
        {
            state.CancellationTokenSource.Cancel();
            state.CancellationTokenSource.Dispose();
            state.Subscription.IsActive = false;

            _logger.LogInformation("Unsubscribed {SubscriptionId}", subscriptionId);

            if (deleteConsumer)
            {
                await DeleteConsumerAsync(state.Subscription.StreamName, state.Subscription.ConsumerName, cancellationToken);
            }
        }
    }

    private JetStreamMessage MapNatsMessage(NatsJSMsg<byte[]> msg, string streamName, string consumerName)
    {
        var headers = new Dictionary<string, string>();
        if (msg.Headers != null)
        {
            foreach (var (key, values) in msg.Headers)
            {
                headers[key] = values.FirstOrDefault() ?? string.Empty;
            }
        }

        return new JetStreamMessage
        {
            Subject = msg.Subject,
            Data = msg.Data ?? Array.Empty<byte>(),
            Headers = headers,
            Sequence = msg.Metadata?.Sequence.Stream ?? 0,
            ConsumerSequence = msg.Metadata?.Sequence.Consumer ?? 0,
            Timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow,
            DeliveryCount = (int)(msg.Metadata?.NumDelivered ?? 1),
            Stream = streamName,
            Consumer = consumerName,
            AckContext = msg
        };
    }

    #endregion

    #region Message Acknowledgement

    public async Task AckMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is NatsJSMsg<byte[]> msg)
        {
            await msg.AckAsync(cancellationToken: cancellationToken);
            _logger.LogDebug("Acknowledged message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
    }

    public async Task NakMessageAsync(JetStreamMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is NatsJSMsg<byte[]> msg)
        {
            if (delay.HasValue)
            {
                await msg.NakAsync(delay: delay.Value, cancellationToken: cancellationToken);
            }
            else
            {
                await msg.NakAsync(cancellationToken: cancellationToken);
            }
            _logger.LogDebug("NAK'd message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
    }

    public async Task InProgressAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is NatsJSMsg<byte[]> msg)
        {
            await msg.AckProgressAsync(cancellationToken: cancellationToken);
            _logger.LogDebug("Extended ack deadline. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
    }

    public async Task TerminateMessageAsync(JetStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message.AckContext is NatsJSMsg<byte[]> msg)
        {
            await msg.AckTerminateAsync(cancellationToken: cancellationToken);
            _logger.LogDebug("Terminated message. Subject: {Subject}, Sequence: {Sequence}", message.Subject, message.Sequence);
        }
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
        var streamName = await FindStreamForSubjectAsync(subject, cancellationToken);
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

    private async Task<string?> FindStreamForSubjectAsync(string subject, CancellationToken cancellationToken)
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
            var info = stream.Info;
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

    public async Task<IReadOnlyList<StreamInfo>> GetAllStreamsAsync(CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var streams = new List<StreamInfo>();
        
        await foreach (var stream in _jetStream!.ListStreamsAsync(cancellationToken: cancellationToken))
        {
            streams.Add(MapStreamInfo(stream.Info));
        }

        return streams;
    }

    public async Task<IReadOnlyList<ConsumerInfo>> GetAllConsumersAsync(string streamName, CancellationToken cancellationToken = default)
    {
        EnsureJetStreamAvailable();

        var stream = await _jetStream!.GetStreamAsync(streamName, cancellationToken: cancellationToken);
        var consumers = new List<ConsumerInfo>();

        await foreach (var consumer in stream.ListConsumersAsync(cancellationToken: cancellationToken))
        {
            consumers.Add(MapConsumerInfo(consumer.Info));
        }

        return consumers;
    }

    #endregion

    #region Helpers

    private void EnsureJetStreamAvailable()
    {
        if (_jetStream == null)
        {
            throw new InvalidOperationException("JetStream is not available. Ensure the service is initialized and JetStream is enabled.");
        }
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancel all subscriptions
        foreach (var (_, state) in _subscriptions)
        {
            state.CancellationTokenSource.Cancel();
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

        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        _initLock.Dispose();

        _logger.LogInformation("JetStream NATS service disposed");
    }

    #endregion

    #region Internal Types

    private class SubscriptionState
    {
        public required JetStreamSubscription Subscription { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
        public required Func<JetStreamMessage, Task> Handler { get; init; }
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
