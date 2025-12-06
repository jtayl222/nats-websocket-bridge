namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Interface for NATS operations.
/// This interface is deprecated. Use IJetStreamNatsService for new implementations.
/// </summary>
[Obsolete("Use IJetStreamNatsService instead. This interface uses core NATS pub/sub which doesn't provide delivery guarantees.")]
public interface INatsService
{
    /// <summary>
    /// Check if connected to NATS
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Publish a message to a NATS subject.
    /// </summary>
    /// <param name="subject">NATS subject</param>
    /// <param name="data">Message data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Obsolete("Use IJetStreamNatsService.PublishAsync() for guaranteed delivery with JetStream.")]
    Task PublishAsync(string subject, byte[] data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Subscribe to a NATS subject.
    /// </summary>
    /// <param name="subject">NATS subject</param>
    /// <param name="handler">Message handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription ID for unsubscribing</returns>
    [Obsolete("Use IJetStreamNatsService.SubscribeAsync() for durable consumers with acknowledgement.")]
    Task<string> SubscribeAsync(string subject, Func<string, byte[], Task> handler, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unsubscribe from a subject
    /// </summary>
    /// <param name="subscriptionId">Subscription ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Obsolete("Use IJetStreamNatsService.UnsubscribeAsync() instead.")]
    Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Publish a message to JetStream
    /// </summary>
    /// <param name="subject">NATS subject</param>
    /// <param name="data">Message data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Obsolete("Use IJetStreamNatsService.PublishAsync() instead.")]
    Task PublishToJetStreamAsync(string subject, byte[] data, CancellationToken cancellationToken = default);
}
