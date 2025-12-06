/**
 * @file temperature_sensor.cpp
 * @brief Industrial temperature sensor example with proper error handling
 *
 * Demonstrates:
 * - Configuration from environment/file
 * - Robust error handling
 * - Reconnection behavior
 * - Multiple subscription patterns
 * - Structured telemetry data
 */

#include <gateway/gateway_device.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <csignal>
#include <atomic>
#include <cmath>
#include <cstdlib>
#include <fstream>

std::atomic<bool> running{true};

void signalHandler(int) {
    std::cout << "\nShutdown requested..." << std::endl;
    running = false;
}

// Simulated temperature sensor
class TemperatureSensor {
public:
    TemperatureSensor(double baseTemp = 25.0, double variance = 5.0)
        : baseTemp_(baseTemp), variance_(variance), trend_(0.0) {}

    double read() {
        // Simulate temperature with noise and trend
        double noise = (std::rand() % 100 - 50) / 100.0 * variance_;
        trend_ += (std::rand() % 100 - 50) / 1000.0;
        trend_ = std::max(-2.0, std::min(2.0, trend_));  // Clamp trend

        lastReading_ = baseTemp_ + trend_ + noise;
        readCount_++;
        return lastReading_;
    }

    double getLastReading() const { return lastReading_; }
    int getReadCount() const { return readCount_; }

    // Check for anomalies (simulated)
    bool isAnomalous() const {
        return std::abs(lastReading_ - baseTemp_) > variance_ * 1.5;
    }

private:
    double baseTemp_;
    double variance_;
    double trend_;
    double lastReading_ = 0.0;
    int readCount_ = 0;
};

// Load configuration from environment or defaults
gateway::GatewayConfig loadConfig() {
    gateway::GatewayConfig config;

    // Gateway URL - from environment or default
    const char* url = std::getenv("GATEWAY_URL");
    config.gatewayUrl = url ? url : "wss://localhost:5000/ws";

    // Device ID - from environment or default
    const char* deviceId = std::getenv("DEVICE_ID");
    config.deviceId = deviceId ? deviceId : "sensor-temp-001";

    // Auth token - MUST be provided
    const char* token = std::getenv("DEVICE_TOKEN");
    if (!token) {
        std::cerr << "Warning: DEVICE_TOKEN not set, using test token" << std::endl;
        config.authToken = "test-token-temp-001";
    } else {
        config.authToken = token;
    }

    config.deviceType = gateway::DeviceType::Sensor;

    // Connection settings
    config.connectTimeout = gateway::Duration{15000};
    config.authTimeout = gateway::Duration{30000};

    // TLS settings (for production, verify certificates)
    const char* insecure = std::getenv("GATEWAY_INSECURE");
    config.tls.verifyPeer = !(insecure && std::string(insecure) == "true");

    // Reconnection settings
    config.reconnect.enabled = true;
    config.reconnect.maxAttempts = 0;  // Unlimited
    config.reconnect.initialDelay = gateway::Duration{1000};
    config.reconnect.maxDelay = gateway::Duration{60000};

    // Heartbeat
    config.heartbeat.enabled = true;
    config.heartbeat.interval = gateway::Duration{30000};

    return config;
}

int main(int argc, char* argv[]) {
    std::signal(SIGINT, signalHandler);
    std::signal(SIGTERM, signalHandler);

    std::cout << "=== Temperature Sensor Example ===" << std::endl;
    std::cout << "SDK Version: " << gateway::GatewayClient::getVersion() << std::endl;

    // Load configuration
    auto config = loadConfig();
    std::cout << "Device ID: " << config.deviceId << std::endl;
    std::cout << "Gateway: " << config.gatewayUrl << std::endl;

    // Create custom logger
    auto logger = std::make_shared<gateway::ConsoleLogger>(config.logging);
    logger->setLevel(gateway::LogLevel::Info);

    // Create client
    gateway::GatewayClient client(config, logger);

    // Set up callbacks
    client.onConnected([&config] {
        std::cout << "[" << config.deviceId << "] Connected to gateway" << std::endl;
    });

    client.onDisconnected([](gateway::ErrorCode code, const std::string& reason) {
        std::cout << "[WARN] Disconnected: " << reason
                 << " (code: " << gateway::errorCodeToString(code) << ")" << std::endl;
    });

    client.onReconnecting([](uint32_t attempt) {
        std::cout << "[INFO] Reconnecting (attempt " << attempt << ")..." << std::endl;
    });

    client.onError([](gateway::ErrorCode code, const std::string& message) {
        std::cerr << "[ERROR] " << message
                 << " (code: " << gateway::errorCodeToString(code) << ")" << std::endl;
    });

    // Connect
    std::cout << "Connecting..." << std::endl;
    if (!client.connect()) {
        std::cerr << "Failed to connect to gateway" << std::endl;
        return 1;
    }

    // Print device info
    if (auto deviceInfo = client.getDeviceInfo()) {
        std::cout << "Authenticated as: " << deviceInfo->deviceId << std::endl;
        std::cout << "Allowed publish topics: ";
        for (const auto& topic : deviceInfo->allowedPublishTopics) {
            std::cout << topic << " ";
        }
        std::cout << std::endl;
    }

    // Subscribe to configuration updates
    client.subscribe("config." + config.deviceId + ".>",
        [](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            std::cout << "[CONFIG] Update received on " << subject << std::endl;
            // Handle configuration changes
        });

    // Subscribe to commands
    client.subscribe("commands." + config.deviceId + ".>",
        [](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            std::cout << "[COMMAND] " << subject << std::endl;

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();
                std::cout << "  Action: " << action << std::endl;
            }
        });

    // Create sensor
    TemperatureSensor sensor(25.0, 3.0);

    // Telemetry interval (5 seconds)
    const auto telemetryInterval = std::chrono::seconds(5);
    auto lastTelemetry = std::chrono::steady_clock::now();

    std::cout << "Starting telemetry loop..." << std::endl;

    while (running) {
        // Poll for messages
        client.poll(gateway::Duration{100});

        // Check connection
        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        // Time for telemetry?
        auto now = std::chrono::steady_clock::now();
        if (now - lastTelemetry >= telemetryInterval) {
            lastTelemetry = now;

            // Read sensor
            double temp = sensor.read();

            // Build telemetry payload
            gateway::JsonValue telemetry = gateway::JsonValue::object();
            telemetry["temperature"] = temp;
            telemetry["unit"] = "celsius";
            telemetry["reading_number"] = sensor.getReadCount();

            // Add timestamp
            auto timestamp = std::chrono::system_clock::now();
            auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                timestamp.time_since_epoch()).count();
            telemetry["timestamp_ms"] = static_cast<int64_t>(ms);

            // Publish to telemetry topic
            auto result = client.publish("telemetry." + config.deviceId + ".temperature", telemetry);

            if (result.ok()) {
                std::cout << "Telemetry: temp=" << temp << "C (reading #"
                         << sensor.getReadCount() << ")" << std::endl;
            } else {
                std::cerr << "Failed to publish: " << result.errorMessage() << std::endl;
            }

            // Check for anomalies and publish alerts
            if (sensor.isAnomalous()) {
                gateway::JsonValue alert = gateway::JsonValue::object();
                alert["type"] = "temperature_anomaly";
                alert["value"] = temp;
                alert["threshold"] = 25.0 + 3.0 * 1.5;
                alert["severity"] = "warning";

                client.publish("alerts." + config.deviceId + ".temperature", alert);
                std::cout << "[ALERT] Temperature anomaly detected: " << temp << "C" << std::endl;
            }
        }
    }

    // Print final stats
    auto stats = client.getStats();
    std::cout << "\n=== Final Statistics ===" << std::endl;
    std::cout << "Messages sent: " << stats.messagesSent << std::endl;
    std::cout << "Messages received: " << stats.messagesReceived << std::endl;
    std::cout << "Bytes sent: " << stats.bytesSent << std::endl;
    std::cout << "Bytes received: " << stats.bytesReceived << std::endl;
    std::cout << "Reconnects: " << stats.reconnectCount << std::endl;
    std::cout << "Errors: " << stats.errorCount << std::endl;

    // Disconnect
    client.disconnect();
    std::cout << "Goodbye!" << std::endl;

    return 0;
}
