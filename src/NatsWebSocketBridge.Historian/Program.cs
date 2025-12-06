using NatsWebSocketBridge.Historian.Configuration;
using NatsWebSocketBridge.Historian.Services;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Service", "Historian")
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    Log.Information("Starting Historian service...");

    var builder = Host.CreateApplicationBuilder(args);

    // Add Serilog
    builder.Services.AddSerilog();

    // Configure options
    builder.Services.Configure<HistorianOptions>(
        builder.Configuration.GetSection(HistorianOptions.SectionName));
    builder.Services.Configure<NatsOptions>(
        builder.Configuration.GetSection(NatsOptions.SectionName));
    builder.Services.Configure<HistorianJetStreamOptions>(
        builder.Configuration.GetSection(HistorianJetStreamOptions.SectionName));
    builder.Services.Configure<ArchivalOptions>(
        builder.Configuration.GetSection(ArchivalOptions.SectionName));

    // Register services
    builder.Services.AddSingleton<IChecksumService, Sha256ChecksumService>();
    builder.Services.AddSingleton<IHistorianRepository, TimescaleRepository>();
    builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
    builder.Services.AddHostedService<HistorianService>();
    builder.Services.AddHostedService<ArchivalService>();

    // Configure Prometheus metrics server
    var metricsPort = builder.Configuration.GetValue<int>("Metrics:Port", 9091);

    var host = builder.Build();

    // Start Prometheus metrics server
    var metricsServer = new KestrelMetricServer(port: metricsPort);
    metricsServer.Start();
    Log.Information("Prometheus metrics server started on port {Port}", metricsPort);

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Historian service terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
