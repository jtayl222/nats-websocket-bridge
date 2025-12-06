using System.Diagnostics;
using System.Diagnostics.Metrics;
using Prometheus;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Gateway metrics for Prometheus monitoring.
/// Exposes metrics for connections, authentication, messages, and NATS operations.
/// </summary>
public interface IGatewayMetrics
{
    // Connection metrics
    void ConnectionOpened(string deviceType);
    void ConnectionClosed(string deviceType, string reason);
    void RecordConnectionDuration(string deviceType, double durationSeconds);

    // Authentication metrics
    void AuthAttempt(string status);
    void RecordAuthDuration(double durationSeconds);

    // Message metrics
    void MessageReceived(string messageType, string? deviceId = null);
    void MessageSent(string messageType, string? deviceId = null);
    void RecordMessageSize(string direction, int bytes);
    void RecordMessageProcessingDuration(string messageType, double durationSeconds);

    // Rate limiting metrics
    void RateLimitRejection(string deviceId);

    // NATS metrics
    void NatsPublish(string? stream = null);
    void NatsPublishError(string? stream = null);
    void NatsSubscribe();
    void RecordNatsLatency(string operation, double durationSeconds);

    // Authorization metrics
    void AuthorizationCheck(string operation, bool allowed);
}

/// <summary>
/// Prometheus implementation of gateway metrics
/// </summary>
public class GatewayMetrics : IGatewayMetrics
{
    // ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new("NatsWebSocketBridge.Gateway", "1.0.0");

    // Connection metrics
    private readonly Counter _connectionsTotal;
    private readonly Gauge _connectionsActive;
    private readonly Counter _disconnectionsTotal;
    private readonly Histogram _connectionDuration;

    // Authentication metrics
    private readonly Counter _authAttemptsTotal;
    private readonly Histogram _authDuration;

    // Message metrics
    private readonly Counter _messagesReceivedTotal;
    private readonly Counter _messagesSentTotal;
    private readonly Histogram _messageSize;
    private readonly Histogram _messageProcessingDuration;

    // Rate limiting metrics
    private readonly Counter _rateLimitRejectionsTotal;

    // NATS metrics
    private readonly Counter _natsPublishTotal;
    private readonly Counter _natsPublishErrorsTotal;
    private readonly Counter _natsSubscribeTotal;
    private readonly Histogram _natsLatency;

    // Authorization metrics
    private readonly Counter _authorizationChecksTotal;

    public GatewayMetrics()
    {
        // Connection metrics
        _connectionsTotal = Metrics.CreateCounter(
            "gateway_websocket_connections_total",
            "Total number of WebSocket connections",
            new CounterConfiguration
            {
                LabelNames = new[] { "device_type" }
            });

        _connectionsActive = Metrics.CreateGauge(
            "gateway_websocket_connections_active",
            "Current number of active WebSocket connections",
            new GaugeConfiguration
            {
                LabelNames = new[] { "device_type" }
            });

        _disconnectionsTotal = Metrics.CreateCounter(
            "gateway_websocket_disconnections_total",
            "Total number of WebSocket disconnections",
            new CounterConfiguration
            {
                LabelNames = new[] { "device_type", "reason" }
            });

        _connectionDuration = Metrics.CreateHistogram(
            "gateway_websocket_connection_duration_seconds",
            "Duration of WebSocket connections in seconds",
            new HistogramConfiguration
            {
                LabelNames = new[] { "device_type" },
                Buckets = new double[] { 1, 5, 15, 30, 60, 300, 600, 1800, 3600, 7200, 14400, 28800, 86400 }
            });

        // Authentication metrics
        _authAttemptsTotal = Metrics.CreateCounter(
            "gateway_auth_attempts_total",
            "Total authentication attempts",
            new CounterConfiguration
            {
                LabelNames = new[] { "status" }
            });

        _authDuration = Metrics.CreateHistogram(
            "gateway_auth_duration_seconds",
            "Duration of authentication operations",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) // 1ms to ~1s
            });

        // Message metrics
        _messagesReceivedTotal = Metrics.CreateCounter(
            "gateway_messages_received_total",
            "Total messages received from devices",
            new CounterConfiguration
            {
                LabelNames = new[] { "type" }
            });

        _messagesSentTotal = Metrics.CreateCounter(
            "gateway_messages_sent_total",
            "Total messages sent to devices",
            new CounterConfiguration
            {
                LabelNames = new[] { "type" }
            });

        _messageSize = Metrics.CreateHistogram(
            "gateway_message_size_bytes",
            "Size of messages in bytes",
            new HistogramConfiguration
            {
                LabelNames = new[] { "direction" },
                Buckets = new double[] { 64, 256, 1024, 4096, 16384, 65536, 262144, 1048576 }
            });

        _messageProcessingDuration = Metrics.CreateHistogram(
            "gateway_message_processing_duration_seconds",
            "Duration of message processing",
            new HistogramConfiguration
            {
                LabelNames = new[] { "type" },
                Buckets = Histogram.ExponentialBuckets(0.0001, 2, 15) // 0.1ms to ~3s
            });

        // Rate limiting metrics
        _rateLimitRejectionsTotal = Metrics.CreateCounter(
            "gateway_rate_limit_rejections_total",
            "Total messages rejected due to rate limiting",
            new CounterConfiguration
            {
                LabelNames = new[] { "device_id" }
            });

        // NATS metrics
        _natsPublishTotal = Metrics.CreateCounter(
            "gateway_nats_publish_total",
            "Total messages published to NATS",
            new CounterConfiguration
            {
                LabelNames = new[] { "stream" }
            });

        _natsPublishErrorsTotal = Metrics.CreateCounter(
            "gateway_nats_publish_errors_total",
            "Total NATS publish errors",
            new CounterConfiguration
            {
                LabelNames = new[] { "stream" }
            });

        _natsSubscribeTotal = Metrics.CreateCounter(
            "gateway_nats_subscribe_total",
            "Total NATS subscriptions created");

        _natsLatency = Metrics.CreateHistogram(
            "gateway_nats_latency_seconds",
            "Latency of NATS operations",
            new HistogramConfiguration
            {
                LabelNames = new[] { "operation" },
                Buckets = Histogram.ExponentialBuckets(0.00001, 2, 20) // 10μs to ~10s
            });

        // Authorization metrics
        _authorizationChecksTotal = Metrics.CreateCounter(
            "gateway_authorization_checks_total",
            "Total authorization checks",
            new CounterConfiguration
            {
                LabelNames = new[] { "operation", "result" }
            });
    }

    // Connection methods
    public void ConnectionOpened(string deviceType)
    {
        _connectionsTotal.WithLabels(deviceType).Inc();
        _connectionsActive.WithLabels(deviceType).Inc();
    }

    public void ConnectionClosed(string deviceType, string reason)
    {
        _connectionsActive.WithLabels(deviceType).Dec();
        _disconnectionsTotal.WithLabels(deviceType, reason).Inc();
    }

    public void RecordConnectionDuration(string deviceType, double durationSeconds)
    {
        _connectionDuration.WithLabels(deviceType).Observe(durationSeconds);
    }

    // Authentication methods
    public void AuthAttempt(string status)
    {
        _authAttemptsTotal.WithLabels(status).Inc();
    }

    public void RecordAuthDuration(double durationSeconds)
    {
        _authDuration.Observe(durationSeconds);
    }

    // Message methods
    public void MessageReceived(string messageType, string? deviceId = null)
    {
        _messagesReceivedTotal.WithLabels(messageType).Inc();
    }

    public void MessageSent(string messageType, string? deviceId = null)
    {
        _messagesSentTotal.WithLabels(messageType).Inc();
    }

    public void RecordMessageSize(string direction, int bytes)
    {
        _messageSize.WithLabels(direction).Observe(bytes);
    }

    public void RecordMessageProcessingDuration(string messageType, double durationSeconds)
    {
        _messageProcessingDuration.WithLabels(messageType).Observe(durationSeconds);
    }

    // Rate limiting methods
    public void RateLimitRejection(string deviceId)
    {
        _rateLimitRejectionsTotal.WithLabels(deviceId).Inc();
    }

    // NATS methods
    public void NatsPublish(string? stream = null)
    {
        _natsPublishTotal.WithLabels(stream ?? "core").Inc();
    }

    public void NatsPublishError(string? stream = null)
    {
        _natsPublishErrorsTotal.WithLabels(stream ?? "core").Inc();
    }

    public void NatsSubscribe()
    {
        _natsSubscribeTotal.Inc();
    }

    public void RecordNatsLatency(string operation, double durationSeconds)
    {
        _natsLatency.WithLabels(operation).Observe(durationSeconds);
    }

    // Authorization methods
    public void AuthorizationCheck(string operation, bool allowed)
    {
        _authorizationChecksTotal.WithLabels(operation, allowed ? "allowed" : "denied").Inc();
    }
}
