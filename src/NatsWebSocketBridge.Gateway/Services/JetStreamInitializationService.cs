using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Background service to initialize JetStream NATS connection and streams at startup
/// </summary>
public class JetStreamInitializationService : IHostedService
{
    private readonly IJetStreamNatsService _jetStreamService;
    private readonly ILogger<JetStreamInitializationService> _logger;

    public JetStreamInitializationService(
        IJetStreamNatsService jetStreamService,
        ILogger<JetStreamInitializationService> logger)
    {
        _jetStreamService = jetStreamService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing JetStream NATS service...");
        
        try
        {
            await _jetStreamService.InitializeAsync(cancellationToken);
            
            _logger.LogInformation("JetStream NATS service initialized successfully. Connected: {IsConnected}, JetStream Available: {JetStreamAvailable}",
                _jetStreamService.IsConnected, _jetStreamService.IsJetStreamAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JetStream NATS service");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JetStream initialization service stopping");
        return Task.CompletedTask;
    }
}
