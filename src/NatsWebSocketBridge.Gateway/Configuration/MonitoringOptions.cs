namespace NatsWebSocketBridge.Gateway.Configuration;

/// <summary>
/// Configuration options for monitoring (Prometheus, Loki, OpenTelemetry)
/// </summary>
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Enable or disable metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable or disable distributed tracing
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Service name for telemetry
    /// </summary>
    public string ServiceName { get; set; } = "nats-websocket-gateway";

    /// <summary>
    /// Service version for telemetry
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Environment name (production, staging, development)
    /// </summary>
    public string Environment { get; set; } = "development";

    /// <summary>
    /// Prometheus configuration
    /// </summary>
    public PrometheusOptions Prometheus { get; set; } = new();

    /// <summary>
    /// Loki configuration
    /// </summary>
    public LokiOptions Loki { get; set; } = new();
}

/// <summary>
/// Prometheus-specific options
/// </summary>
public class PrometheusOptions
{
    /// <summary>
    /// Enable Prometheus metrics endpoint
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path for the Prometheus metrics endpoint
    /// </summary>
    public string Endpoint { get; set; } = "/metrics";

    /// <summary>
    /// Include runtime metrics (.NET GC, thread pool, etc.)
    /// </summary>
    public bool IncludeRuntimeMetrics { get; set; } = true;

    /// <summary>
    /// Include HTTP metrics
    /// </summary>
    public bool IncludeHttpMetrics { get; set; } = true;
}

/// <summary>
/// Loki-specific options
/// </summary>
public class LokiOptions
{
    /// <summary>
    /// Enable Loki log shipping
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Loki server URL (e.g., http://loki:3100)
    /// </summary>
    public string Url { get; set; } = "http://localhost:3100";

    /// <summary>
    /// Additional labels to add to all log entries
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new()
    {
        { "app", "nats-websocket-gateway" }
    };

    /// <summary>
    /// Batch posting interval in seconds
    /// </summary>
    public int BatchPostingIntervalSeconds { get; set; } = 2;
}
