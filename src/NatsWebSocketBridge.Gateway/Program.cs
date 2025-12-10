using NatsWebSocketBridge.Gateway.Auth;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Services;
using NatsWebSocketBridge.Gateway.Handlers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting NATS WebSocket Gateway");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from configuration
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var monitoringOptions = context.Configuration
            .GetSection(MonitoringOptions.SectionName)
            .Get<MonitoringOptions>() ?? new MonitoringOptions();

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("service", monitoringOptions.ServiceName)
            .Enrich.WithProperty("version", monitoringOptions.ServiceVersion)
            .Enrich.WithProperty("environment", monitoringOptions.Environment)
            .WriteTo.Console(new CompactJsonFormatter());

        // Add Loki sink if enabled
        if (monitoringOptions.Loki.Enabled)
        {
            var lokiLabels = monitoringOptions.Loki.Labels
                .Select(kv => new LokiLabel { Key = kv.Key, Value = kv.Value })
                .ToArray();

            configuration.WriteTo.GrafanaLoki(
                monitoringOptions.Loki.Url,
                labels: lokiLabels,
                textFormatter: new CompactJsonFormatter());
        }
    });

    // Add configuration
    builder.Services.Configure<GatewayOptions>(
        builder.Configuration.GetSection(GatewayOptions.SectionName));
    builder.Services.Configure<NatsOptions>(
        builder.Configuration.GetSection(NatsOptions.SectionName));
    builder.Services.Configure<JetStreamOptions>(
        builder.Configuration.GetSection(JetStreamOptions.SectionName));
    builder.Services.Configure<MonitoringOptions>(
        builder.Configuration.GetSection(MonitoringOptions.SectionName));

    var monitoringConfig = builder.Configuration
        .GetSection(MonitoringOptions.SectionName)
        .Get<MonitoringOptions>() ?? new MonitoringOptions();

    // Add Gateway metrics service
    builder.Services.AddSingleton<IGatewayMetrics, GatewayMetrics>();

    // Configure OpenTelemetry
    if (monitoringConfig.EnableMetrics || monitoringConfig.EnableTracing)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: monitoringConfig.ServiceName,
                serviceVersion: monitoringConfig.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                { "deployment.environment", monitoringConfig.Environment }
            });

        var otelBuilder = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(monitoringConfig.ServiceName, serviceVersion: monitoringConfig.ServiceVersion));

        if (monitoringConfig.EnableTracing)
        {
            otelBuilder.WithTracing(tracing =>
            {
                tracing
                    .AddSource(GatewayMetrics.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/metrics");
                    })
                    .AddHttpClientInstrumentation();
            });
        }

        if (monitoringConfig.EnableMetrics)
        {
            otelBuilder.WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();
            });
        }
    }

    // Add JWT authentication service
    builder.Services.Configure<JwtOptions>(
        builder.Configuration.GetSection(JwtOptions.SectionName));
    builder.Services.AddSingleton<IJwtDeviceAuthService, JwtDeviceAuthService>();

    // Add core services
    builder.Services.AddSingleton<IDeviceConnectionManager, DeviceConnectionManager>();
    builder.Services.AddSingleton<IMessageValidationService, MessageValidationService>();
    builder.Services.AddSingleton<IMessageThrottlingService, TokenBucketThrottlingService>();
    builder.Services.AddSingleton<IMessageBufferService, MessageBufferService>();

    // Add JetStream NATS service
    builder.Services.AddSingleton<IJetStreamNatsService, JetStreamNatsService>();

    // Add WebSocket handler
    builder.Services.AddScoped<DeviceWebSocketHandler>();

    // Add JetStream initialization service
    builder.Services.AddHostedService<JetStreamInitializationService>();

    // Add OpenAPI support
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress?.ToString());
        };
        // Don't log metrics endpoint requests
        options.GetLevel = (httpContext, elapsed, ex) =>
            httpContext.Request.Path.StartsWithSegments("/metrics")
                ? LogEventLevel.Verbose
                : LogEventLevel.Information;
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Prometheus metrics endpoint
    if (monitoringConfig.Prometheus.Enabled)
    {
        app.UseMetricServer(monitoringConfig.Prometheus.Endpoint);

        if (monitoringConfig.Prometheus.IncludeHttpMetrics)
        {
            app.UseHttpMetrics(options =>
            {
                options.AddCustomLabel("service", context => monitoringConfig.ServiceName);
            });
        }

        Log.Information("Prometheus metrics enabled at {Endpoint}", monitoringConfig.Prometheus.Endpoint);
    }

    // OpenTelemetry Prometheus endpoint (in addition to prometheus-net)
    if (monitoringConfig.EnableMetrics)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    // Enable WebSockets
    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30)
    });

    // Add WebSocket middleware for device connections
    app.UseMiddleware<WebSocketMiddleware>();

    // Health check endpoint
    app.MapGet("/health", (IDeviceConnectionManager connectionManager, IJetStreamNatsService jetStreamService) =>
    {
        return Results.Ok(new
        {
            status = "healthy",
            connectedDevices = connectionManager.ConnectionCount,
            natsConnected = jetStreamService.IsConnected,
            jetStreamAvailable = jetStreamService.IsJetStreamAvailable
        });
    }).WithName("HealthCheck");

    // Readiness endpoint for Kubernetes
    app.MapGet("/ready", (IJetStreamNatsService jetStreamService) =>
    {
        if (!jetStreamService.IsConnected)
        {
            return Results.StatusCode(503);
        }
        return Results.Ok(new { status = "ready" });
    }).WithName("ReadinessCheck");

    // Liveness endpoint for Kubernetes
    app.MapGet("/live", () => Results.Ok(new { status = "alive" }))
        .WithName("LivenessCheck");

    // Get connected devices
    app.MapGet("/devices", (IDeviceConnectionManager connectionManager) =>
    {
        var devices = connectionManager.GetConnectedDevices()
            .Select(id =>
            {
                var ctx = connectionManager.GetDeviceContext(id);
                return new
                {
                    clientId = id,
                    role = ctx?.Role,
                    connectedAt = ctx?.ConnectedAt,
                    expiresAt = ctx?.ExpiresAt
                };
            })
            .ToList();

        return Results.Ok(devices);
    }).WithName("GetConnectedDevices");

    // JetStream stream info endpoint
    app.MapGet("/jetstream/streams", async (IJetStreamNatsService jetStreamService, CancellationToken cancellationToken) =>
    {
        var streams = await jetStreamService.GetAllStreamsAsync(cancellationToken);
        return Results.Ok(streams);
    }).WithName("GetJetStreamStreams");

    // JetStream consumers endpoint
    app.MapGet("/jetstream/streams/{streamName}/consumers", async (string streamName, IJetStreamNatsService jetStreamService, CancellationToken cancellationToken) =>
    {
        var consumers = await jetStreamService.GetAllConsumersAsync(streamName, cancellationToken);
        return Results.Ok(consumers);
    }).WithName("GetJetStreamConsumers");

    // Development-only: Generate JWT tokens for testing
    if (app.Environment.IsDevelopment())
    {
        app.MapPost("/dev/token", (IJwtDeviceAuthService authService, TokenRequest request) =>
        {
            var token = authService.GenerateToken(
                request.ClientId,
                request.Role ?? "device",
                request.Publish ?? new[] { ">" },
                request.Subscribe ?? new[] { ">" },
                request.ExpiryHours.HasValue ? TimeSpan.FromHours(request.ExpiryHours.Value) : null);

            return Results.Ok(new { token });
        }).WithName("GenerateDevToken");

        Log.Warning("Development token endpoint enabled at POST /dev/token");
    }

    Log.Information("Gateway started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Request model for generating development JWT tokens
/// </summary>
internal record TokenRequest(
    string ClientId,
    string? Role = null,
    string[]? Publish = null,
    string[]? Subscribe = null,
    int? ExpiryHours = null);
