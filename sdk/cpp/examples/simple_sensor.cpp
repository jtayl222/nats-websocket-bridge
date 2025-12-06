/**
 * @file simple_sensor.cpp
 * @brief Simple sensor example demonstrating basic SDK usage
 *
 * This is the minimal example for device manufacturers to get started.
 */

#include <gateway/gateway_device.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <csignal>
#include <atomic>

std::atomic<bool> running{true};

void signalHandler(int) {
    running = false;
}

int main(int argc, char* argv[]) {
    // Register signal handler for graceful shutdown
    std::signal(SIGINT, signalHandler);
    std::signal(SIGTERM, signalHandler);

    // Configuration - typically loaded from file or environment
    gateway::GatewayConfig config;
    config.gatewayUrl = "wss://localhost:5000/ws";  // Your gateway URL
    config.deviceId = "sensor-simple-001";
    config.authToken = "your-device-token";
    config.deviceType = gateway::DeviceType::Sensor;

    // Optional: Customize reconnection behavior
    config.reconnect.enabled = true;
    config.reconnect.maxAttempts = 10;

    // Create the client
    gateway::GatewayClient client(config);

    // Set up callbacks (optional but recommended)
    client.onConnected([] {
        std::cout << "Connected to gateway!" << std::endl;
    });

    client.onDisconnected([](gateway::ErrorCode code, const std::string& reason) {
        std::cout << "Disconnected: " << reason << std::endl;
    });

    client.onError([](gateway::ErrorCode code, const std::string& message) {
        std::cerr << "Error: " << message << std::endl;
    });

    // Connect to gateway
    std::cout << "Connecting to gateway..." << std::endl;
    if (!client.connect()) {
        std::cerr << "Failed to connect!" << std::endl;
        return 1;
    }

    std::cout << "Connected and authenticated!" << std::endl;

    // Subscribe to commands for this device
    auto subResult = client.subscribe("commands." + config.deviceId + ".>",
        [](const std::string& subject,
           const gateway::JsonValue& payload,
           const gateway::Message& msg) {
            std::cout << "Received command on " << subject << std::endl;

            // Handle specific commands
            if (subject.find("restart") != std::string::npos) {
                std::cout << "  -> Restart requested" << std::endl;
            } else if (subject.find("configure") != std::string::npos) {
                std::cout << "  -> Configuration update" << std::endl;
            }
        });

    if (subResult.failed()) {
        std::cerr << "Failed to subscribe: " << subResult.errorMessage() << std::endl;
    }

    // Main loop - publish sensor data periodically
    int readingCount = 0;
    while (running && client.isConnected()) {
        // Simulate sensor reading
        double temperature = 20.0 + (std::rand() % 100) / 10.0;
        double humidity = 40.0 + (std::rand() % 400) / 10.0;

        // Create payload
        gateway::JsonValue data = gateway::JsonValue::object();
        data["temperature"] = temperature;
        data["humidity"] = humidity;
        data["reading"] = ++readingCount;
        data["unit"] = "celsius";

        // Publish to sensor topic
        auto result = client.publish("sensors." + config.deviceId + ".readings", data);

        if (result.ok()) {
            std::cout << "Published reading #" << readingCount
                     << " temp=" << temperature
                     << " humidity=" << humidity << std::endl;
        } else {
            std::cerr << "Publish failed: " << result.errorMessage() << std::endl;
        }

        // Poll for incoming messages and send outgoing
        for (int i = 0; i < 50 && running; i++) {
            client.poll(gateway::Duration{100});
        }
    }

    // Cleanup
    std::cout << "Disconnecting..." << std::endl;
    client.disconnect();

    std::cout << "Done. Published " << readingCount << " readings." << std::endl;
    return 0;
}
