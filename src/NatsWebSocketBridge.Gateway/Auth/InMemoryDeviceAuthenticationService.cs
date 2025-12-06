using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NatsWebSocketBridge.Gateway.Configuration;
using NatsWebSocketBridge.Gateway.Models;

namespace NatsWebSocketBridge.Gateway.Auth;

/// <summary>
/// Simple in-memory device authentication service
/// In production, this would connect to a database or identity provider
/// </summary>
public class InMemoryDeviceAuthenticationService : IDeviceAuthenticationService
{
    private readonly ILogger<InMemoryDeviceAuthenticationService> _logger;
    private readonly GatewayOptions _options;
    
    // In-memory device registry (in production, use a database)
    private readonly Dictionary<string, DeviceRegistration> _deviceRegistry = new();
    
    public InMemoryDeviceAuthenticationService(
        ILogger<InMemoryDeviceAuthenticationService> logger,
        IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        // Register some default devices for testing
        RegisterDefaultDevices();
    }
    
    private void RegisterDefaultDevices()
    {
        // Temperature sensors
        RegisterDevice("sensor-temp-001", "temp-sensor-token-001", "sensor",
            new[] { "devices.sensor-temp-001.data", "devices.sensors.temperature", "factory.line1.>" },
            new[] { "devices.sensor-temp-001.commands", "devices.broadcast", "factory.line1.>" });

        RegisterDevice("sensor-temp-002", "temp-sensor-token-002", "sensor",
            new[] { "devices.sensor-temp-002.data", "devices.sensors.temperature" },
            new[] { "devices.sensor-temp-002.commands", "devices.broadcast" });

        // Actuators
        RegisterDevice("actuator-valve-001", "valve-token-001", "actuator",
            new[] { "devices.actuator-valve-001.status" },
            new[] { "devices.actuator-valve-001.commands", "devices.actuators.valves", "devices.broadcast" });

        // Controllers
        RegisterDevice("controller-plc-001", "plc-token-001", "controller",
            new[] { "devices.controller-plc-001.>", "devices.sensors.>" },
            new[] { "devices.controller-plc-001.>", "devices.actuators.>", "devices.broadcast" });

        // =================================================================
        // PACKAGING LINE DEMO DEVICES
        // =================================================================

        // Conveyor Controller - actuator with bidirectional communication
        RegisterDevice("actuator-conveyor-001", "conveyor-token-001", "actuator",
            new[] { "factory.line1.conveyor.status", "factory.line1.status.>", "factory.line1.alerts.>" },
            new[] { "factory.line1.conveyor.cmd", "factory.line1.emergency", "factory.line1.cmd.>" });

        // Vision Scanner - quality inspection sensor
        RegisterDevice("sensor-vision-001", "vision-token-001", "sensor",
            new[] { "factory.line1.quality.>", "factory.line1.status.>", "factory.line1.alerts.>" },
            new[] { "factory.line1.cmd.sensor-vision-001.>", "factory.line1.emergency", "factory.line1.conveyor.status" });

        // E-Stop Button - safety device with broadcast capability
        RegisterDevice("sensor-estop-001", "estop-token-001", "sensor",
            new[] { "factory.line1.eStop", "factory.line1.emergency", "factory.line1.status.>", "factory.line1.alerts.>" },
            new[] { "factory.line1.cmd.sensor-estop-001.>" });

        // Production Counter - counts packages
        RegisterDevice("sensor-counter-001", "counter-token-001", "sensor",
            new[] { "factory.line1.output", "factory.line1.production.>", "factory.line1.batch.>", "factory.line1.status.>", "factory.line1.alerts.>" },
            new[] { "factory.line1.cmd.sensor-counter-001.>", "factory.line1.conveyor.status", "factory.line1.quality.rejects", "factory.line1.emergency" });

        // Line Orchestrator - central controller with broad permissions
        RegisterDevice("controller-orchestrator-001", "orchestrator-token-001", "controller",
            new[] { "factory.line1.>" },  // Can publish to anything on line1
            new[] { "factory.line1.>" }); // Can subscribe to anything on line1

        // HMI Panel - operator interface
        RegisterDevice("hmi-panel-001", "hmi-token-001", "hmi",
            new[] { "factory.line1.cmd.>", "factory.line1.conveyor.cmd" },  // Can send commands
            new[] { "factory.line1.>" });  // Can see everything
    }
    
    /// <summary>
    /// Register a device in the in-memory registry
    /// </summary>
    public void RegisterDevice(string deviceId, string token, string deviceType, 
        string[] publishTopics, string[] subscribeTopics)
    {
        _deviceRegistry[deviceId] = new DeviceRegistration
        {
            DeviceId = deviceId,
            Token = token,
            DeviceType = deviceType,
            AllowedPublishTopics = publishTopics.ToList(),
            AllowedSubscribeTopics = subscribeTopics.ToList()
        };
    }
    
    public Task<AuthenticationResponse> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Authenticating device {DeviceId}", request.DeviceId);
        
        if (string.IsNullOrEmpty(request.DeviceId) || string.IsNullOrEmpty(request.Token))
        {
            _logger.LogWarning("Authentication failed: missing device ID or token");
            return Task.FromResult(new AuthenticationResponse
            {
                Success = false,
                Error = "Device ID and token are required"
            });
        }
        
        if (!_deviceRegistry.TryGetValue(request.DeviceId, out var registration))
        {
            _logger.LogWarning("Authentication failed: device {DeviceId} not found", request.DeviceId);
            return Task.FromResult(new AuthenticationResponse
            {
                Success = false,
                Error = "Device not registered"
            });
        }
        
        if (registration.Token != request.Token)
        {
            _logger.LogWarning("Authentication failed: invalid token for device {DeviceId}", request.DeviceId);
            return Task.FromResult(new AuthenticationResponse
            {
                Success = false,
                Error = "Invalid token"
            });
        }
        
        var deviceInfo = new DeviceInfo
        {
            DeviceId = registration.DeviceId,
            DeviceType = registration.DeviceType,
            IsConnected = true,
            ConnectedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            AllowedPublishTopics = registration.AllowedPublishTopics,
            AllowedSubscribeTopics = registration.AllowedSubscribeTopics
        };
        
        _logger.LogInformation("Device {DeviceId} authenticated successfully", request.DeviceId);
        
        return Task.FromResult(new AuthenticationResponse
        {
            Success = true,
            Device = deviceInfo
        });
    }
    
    public Task<bool> ValidateDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_deviceRegistry.ContainsKey(deviceId));
    }
    
    private class DeviceRegistration
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public List<string> AllowedPublishTopics { get; set; } = new();
        public List<string> AllowedSubscribeTopics { get; set; } = new();
    }
}
