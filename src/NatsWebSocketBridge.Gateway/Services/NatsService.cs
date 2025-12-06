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
/// Legacy NATS service implementation using NATS.Net.
/// This class is deprecated. Use JetStreamNatsService for new implementations.
/// </summary>
[Obsolete("Use JetStreamNatsService instead. This class uses core NATS pub/sub which doesn't provide delivery guarantees.")]
public class NatsService : INatsService, IAsyncDisposable
{
    private readonly ILogger<NatsService> _logger;
    private readonly NatsOptions _natsOptions;
    private NatsConnection? _connection;
    private NatsJSContext? _jetStream;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new();
    private bool _disposed;
    
    public bool IsConnected => _connection?.ConnectionState == NatsConnectionState.Open;
    
    public NatsService(
        ILogger<NatsService> logger,
        IOptions<NatsOptions> natsOptions)
    {
        _logger = logger;
        _natsOptions = natsOptions.Value;
    }
    
    /// <summary>
    /// Initialize the NATS connection
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
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
        
        if (_natsOptions.UseJetStream)
        {
            try
            {
                _jetStream = new NatsJSContext(_connection);
                
                // Ensure stream exists
                await EnsureStreamExistsAsync(cancellationToken);
                
                _logger.LogInformation("JetStream context initialized for stream {StreamName}", _natsOptions.StreamName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JetStream not available, falling back to core NATS");
                _jetStream = null;
            }
        }
        
        _logger.LogInformation("Connected to NATS server");
    }
    
    private async Task EnsureStreamExistsAsync(CancellationToken cancellationToken)
    {
        if (_jetStream == null)
        {
            return;
        }
        
        try
        {
            // Try to get existing stream
            await _jetStream.GetStreamAsync(_natsOptions.StreamName, cancellationToken: cancellationToken);
            _logger.LogInformation("Stream {StreamName} already exists", _natsOptions.StreamName);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream doesn't exist, create it
            _logger.LogInformation("Creating stream {StreamName}", _natsOptions.StreamName);
            
            var streamConfig = new StreamConfig(
                name: _natsOptions.StreamName,
                subjects: new[] { "devices.>" })
            {
                Storage = StreamConfigStorage.Memory,
                Retention = StreamConfigRetention.Limits,
                MaxMsgs = 100000,
                MaxBytes = 100 * 1024 * 1024 // 100MB
            };
            
            await _jetStream.CreateStreamAsync(streamConfig, cancellationToken);
            _logger.LogInformation("Stream {StreamName} created", _natsOptions.StreamName);
        }
    }
    
    public async Task PublishAsync(string subject, byte[] data, CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("NATS connection not initialized");
        }
        
        await _connection.PublishAsync(subject, data, cancellationToken: cancellationToken);
        _logger.LogDebug("Published message to {Subject}", subject);
    }
    
    public async Task<string> SubscribeAsync(string subject, Func<string, byte[], Task> handler, CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("NATS connection not initialized");
        }
        
        var subscriptionId = Guid.NewGuid().ToString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subscriptions[subscriptionId] = cts;
        
        _logger.LogInformation("Subscribing to {Subject} with ID {SubscriptionId}", subject, subscriptionId);
        
        // Start subscription in background
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token))
                {
                    try
                    {
                        await handler(msg.Subject, msg.Data ?? Array.Empty<byte>());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling message from {Subject}", msg.Subject);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Subscription {SubscriptionId} cancelled", subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in subscription {SubscriptionId}", subscriptionId);
            }
        }, cts.Token);
        
        return subscriptionId;
    }
    
    public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryRemove(subscriptionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Unsubscribed {SubscriptionId}", subscriptionId);
        }
        
        return Task.CompletedTask;
    }
    
    public async Task PublishToJetStreamAsync(string subject, byte[] data, CancellationToken cancellationToken = default)
    {
        if (_jetStream != null)
        {
            await _jetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
            _logger.LogDebug("Published message to JetStream {Subject}", subject);
        }
        else
        {
            // Fallback to core NATS
            await PublishAsync(subject, data, cancellationToken);
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        foreach (var kvp in _subscriptions)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _subscriptions.Clear();
        
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
        
        _logger.LogInformation("NATS service disposed");
    }
}
