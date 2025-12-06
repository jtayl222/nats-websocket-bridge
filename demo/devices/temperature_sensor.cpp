/**
 * @file temperature_sensor.cpp
 * @brief Temperature sensor simulator for packaging line demo
 *
 * Simulates a temperature sensor monitoring the packaging environment.
 * Publishes readings to factory.line1.temp with threshold alerts.
 *
 * Demo features:
 * - Realistic temperature with noise and drift
 * - Anomaly injection via command
 * - Threshold-based alerts
 * - JetStream persistence verification
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "sensor-temp-001";
const std::string TOKEN = "temp-sensor-token-001";
const std::string PUBLISH_SUBJECT = "factory.line1.temp";
const std::string ALERTS_SUBJECT = "factory.line1.alerts";
const int PUBLISH_INTERVAL_MS = 5000;

// Thresholds
const double TEMP_WARNING = 75.0;
const double TEMP_CRITICAL = 80.0;
const double TEMP_MIN = 60.0;
const double TEMP_MAX = 85.0;

int main() {
    installSignalHandlers();
    printBanner("TEMPERATURE SENSOR");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Sensor);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("Publish subject: " + PUBLISH_SUBJECT);
    printStatus("Publish interval: " + std::to_string(PUBLISH_INTERVAL_MS) + "ms");

    // Create client
    GatewayClient client(config);

    // Simulated temperature (base 72°F, ±2°F noise, slight drift)
    SimulatedValue temperature(72.0, 0.5, 0.01);
    Random rng;

    // Track alerts
    bool inWarning = false;
    bool inCritical = false;
    int readingCount = 0;

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");

        // Publish online status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["type"] = "temperature_sensor";
        status["location"] = "Packaging Room A";
        status["batch"] = demoConfig.batchId;

        client.publish("factory.line1.status." + DEVICE_ID, status);
    });

    client.onDisconnected([](ErrorCode code, const std::string& reason) {
        printWarning("Disconnected: " + reason);
    });

    client.onReconnecting([](uint32_t attempt) {
        printStatus("Reconnecting (attempt " + std::to_string(attempt) + ")...");
    });

    client.onError([](ErrorCode code, const std::string& message) {
        printError(message);
    });

    // Connect
    printStatus("Connecting to gateway...");
    if (!client.connect()) {
        printError("Failed to connect to gateway!");
        return 1;
    }

    // Subscribe to commands for this sensor
    client.subscribe("factory.line1.cmd." + DEVICE_ID + ".>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printReceive(subject, "Command received");

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "inject_anomaly") {
                    double magnitude = payload.contains("magnitude") ?
                                       payload["magnitude"].asDouble() : 10.0;
                    int duration = payload.contains("duration") ?
                                   static_cast<int>(payload["duration"].asInt()) : 30000;

                    temperature.injectAnomaly(magnitude, duration);
                    printWarning("Anomaly injected: +" + std::to_string(magnitude) +
                                "°F for " + std::to_string(duration/1000) + "s");

                } else if (action == "set_base") {
                    if (payload.contains("value")) {
                        double newBase = payload["value"].asDouble();
                        temperature.setBase(newBase);
                        printStatus("Base temperature set to " + std::to_string(newBase) + "°F");
                    }

                } else if (action == "status") {
                    // Report current status
                    JsonValue status = JsonValue::object();
                    status["temperature"] = temperature.getBase();
                    status["reading_count"] = readingCount;
                    status["in_warning"] = inWarning;
                    status["in_critical"] = inCritical;

                    client.publish("factory.line1.status." + DEVICE_ID, status);
                }
            }
        });

    // Subscribe to emergency stop
    client.subscribe("factory.line1.emergency",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printAlert("emergency", "Emergency stop received!");
            // In a real device, this would trigger safe state
        });

    printStatus("Starting temperature monitoring...\n");

    // Main loop
    auto lastPublish = std::chrono::steady_clock::now();

    while (g_running) {
        client.poll(Duration{100});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastPublish);

        if (elapsed.count() >= PUBLISH_INTERVAL_MS) {
            lastPublish = now;
            readingCount++;

            // Read temperature
            double temp = temperature.read();

            // Clamp to sensor range
            temp = std::max(TEMP_MIN, std::min(TEMP_MAX, temp));

            // Build telemetry payload
            JsonValue telemetry = JsonValue::object();
            telemetry["value"] = temp;
            telemetry["unit"] = "fahrenheit";
            telemetry["reading"] = readingCount;
            telemetry["timestamp"] = getTimestamp();
            telemetry["batch"] = demoConfig.batchId;
            telemetry["lot"] = demoConfig.lotNumber;

            // Add status
            std::string status = "normal";
            if (temp >= TEMP_CRITICAL) {
                status = "critical";
            } else if (temp >= TEMP_WARNING) {
                status = "warning";
            }
            telemetry["status"] = status;

            // Publish telemetry
            auto result = client.publish(PUBLISH_SUBJECT, telemetry);
            if (result.ok()) {
                std::ostringstream summary;
                summary << std::fixed << std::setprecision(1) << temp << "°F";
                if (status != "normal") {
                    summary << " [" << status << "]";
                }
                printPublish(PUBLISH_SUBJECT, summary.str());
            } else {
                printError("Publish failed: " + result.errorMessage());
            }

            // Check thresholds and send alerts
            if (temp >= TEMP_CRITICAL && !inCritical) {
                inCritical = true;
                inWarning = true;

                JsonValue alert = JsonValue::object();
                alert["severity"] = "critical";
                alert["type"] = "temperature_high";
                alert["value"] = temp;
                alert["threshold"] = TEMP_CRITICAL;
                alert["device"] = DEVICE_ID;
                alert["message"] = "Temperature exceeded critical threshold!";
                alert["timestamp"] = getTimestamp();

                client.publish(ALERTS_SUBJECT + ".critical", alert);
                printAlert("CRITICAL", "Temperature " + std::to_string(temp) +
                          "°F exceeds " + std::to_string(TEMP_CRITICAL) + "°F!");

            } else if (temp >= TEMP_WARNING && !inWarning) {
                inWarning = true;

                JsonValue alert = JsonValue::object();
                alert["severity"] = "warning";
                alert["type"] = "temperature_high";
                alert["value"] = temp;
                alert["threshold"] = TEMP_WARNING;
                alert["device"] = DEVICE_ID;
                alert["message"] = "Temperature exceeded warning threshold";
                alert["timestamp"] = getTimestamp();

                client.publish(ALERTS_SUBJECT + ".warning", alert);
                printAlert("WARNING", "Temperature " + std::to_string(temp) +
                          "°F exceeds " + std::to_string(TEMP_WARNING) + "°F");

            } else if (temp < TEMP_WARNING && inWarning) {
                // Clear warning
                inWarning = false;
                inCritical = false;

                JsonValue clear = JsonValue::object();
                clear["severity"] = "info";
                clear["type"] = "temperature_normal";
                clear["value"] = temp;
                clear["device"] = DEVICE_ID;
                clear["message"] = "Temperature returned to normal";
                clear["timestamp"] = getTimestamp();

                client.publish(ALERTS_SUBJECT + ".info", clear);
                printStatus("Temperature returned to normal: " + std::to_string(temp) + "°F");
            }
        }
    }

    // Publish offline status
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["deviceId"] = DEVICE_ID;
    offline["reading_count"] = readingCount;
    offline["timestamp"] = getTimestamp();

    client.publish("factory.line1.status." + DEVICE_ID, offline);
    client.poll(Duration{200});

    client.disconnect();

    printStatus("Temperature sensor shutdown complete.");
    printStatus("Total readings published: " + std::to_string(readingCount));

    return 0;
}
