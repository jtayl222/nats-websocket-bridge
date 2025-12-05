using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NatsWebSocketBridge.Gateway.Services;

/// <summary>
/// Background service that initializes NATS connection on startup
/// </summary>
public class NatsInitializationService : IHostedService
{
    private readonly ILogger<NatsInitializationService> _logger;
    private readonly NatsService _natsService;
    
    public NatsInitializationService(
        ILogger<NatsInitializationService> logger,
        INatsService natsService)
    {
        _logger = logger;
        _natsService = (NatsService)natsService;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing NATS connection...");
        
        try
        {
            await _natsService.InitializeAsync(cancellationToken);
            _logger.LogInformation("NATS connection established");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize NATS connection");
            // Don't throw - allow the application to start even if NATS is unavailable
            // The NatsService will handle reconnection attempts
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down NATS connection...");
        await _natsService.DisposeAsync();
    }
}
