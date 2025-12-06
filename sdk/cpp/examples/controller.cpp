/**
 * @file controller.cpp
 * @brief Industrial controller (PLC) example
 *
 * Demonstrates:
 * - Subscribing to multiple topics
 * - Aggregating data from multiple sensors
 * - Sending commands to actuators
 * - Simple control logic
 */

#include <gateway/gateway_device.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <csignal>
#include <atomic>
#include <map>
#include <mutex>

std::atomic<bool> running{true};

void signalHandler(int) {
    running = false;
}

// Simple data aggregator
class DataAggregator {
public:
    void update(const std::string& sensorId, const std::string& metric, double value) {
        std::lock_guard<std::mutex> lock(mutex_);

        auto& sensorData = data_[sensorId];
        sensorData[metric] = value;
        sensorData["last_update"] = static_cast<double>(
            std::chrono::duration_cast<std::chrono::seconds>(
                std::chrono::system_clock::now().time_since_epoch()).count());
    }

    double get(const std::string& sensorId, const std::string& metric, double defaultValue = 0.0) const {
        std::lock_guard<std::mutex> lock(mutex_);

        auto sensorIt = data_.find(sensorId);
        if (sensorIt == data_.end()) return defaultValue;

        auto metricIt = sensorIt->second.find(metric);
        if (metricIt == sensorIt->second.end()) return defaultValue;

        return metricIt->second;
    }

    double getAverage(const std::string& metric, double defaultValue = 0.0) const {
        std::lock_guard<std::mutex> lock(mutex_);

        double sum = 0.0;
        int count = 0;

        for (const auto& [sensorId, metrics] : data_) {
            auto it = metrics.find(metric);
            if (it != metrics.end()) {
                sum += it->second;
                count++;
            }
        }

        return count > 0 ? sum / count : defaultValue;
    }

    std::vector<std::string> getSensorIds() const {
        std::lock_guard<std::mutex> lock(mutex_);

        std::vector<std::string> ids;
        for (const auto& [id, _] : data_) {
            ids.push_back(id);
        }
        return ids;
    }

private:
    mutable std::mutex mutex_;
    std::map<std::string, std::map<std::string, double>> data_;
};

// Simple threshold-based controller
class TemperatureController {
public:
    TemperatureController(double setpoint, double hysteresis)
        : setpoint_(setpoint), hysteresis_(hysteresis) {}

    enum class Action { None, Cool, Heat };

    Action evaluate(double temperature) {
        if (temperature > setpoint_ + hysteresis_) {
            cooling_ = true;
            heating_ = false;
            return Action::Cool;
        } else if (temperature < setpoint_ - hysteresis_) {
            heating_ = true;
            cooling_ = false;
            return Action::Heat;
        } else {
            // In deadband, maintain current state
            if (cooling_) return Action::Cool;
            if (heating_) return Action::Heat;
            return Action::None;
        }
    }

    void setSetpoint(double setpoint) { setpoint_ = setpoint; }
    double getSetpoint() const { return setpoint_; }

    static const char* actionToString(Action action) {
        switch (action) {
            case Action::None: return "none";
            case Action::Cool: return "cool";
            case Action::Heat: return "heat";
            default: return "unknown";
        }
    }

private:
    double setpoint_;
    double hysteresis_;
    bool cooling_ = false;
    bool heating_ = false;
};

int main() {
    std::signal(SIGINT, signalHandler);
    std::signal(SIGTERM, signalHandler);

    std::cout << "=== PLC Controller Example ===" << std::endl;

    // Configuration
    gateway::GatewayConfig config;
    config.gatewayUrl = "wss://localhost:5000/ws";
    config.deviceId = "controller-plc-001";
    config.authToken = "controller-token-001";
    config.deviceType = gateway::DeviceType::Controller;

    // Controllers need broader permissions
    config.reconnect.enabled = true;

    // Create client
    gateway::GatewayClient client(config);

    // Data aggregator
    DataAggregator sensors;

    // Temperature controller (setpoint 25C, hysteresis 2C)
    TemperatureController tempController(25.0, 2.0);

    // Track actuator states
    std::map<std::string, std::string> actuatorStates;
    std::mutex actuatorMutex;

    // Connection callback
    client.onConnected([&] {
        std::cout << "Controller connected!" << std::endl;

        // Publish controller status
        gateway::JsonValue status = gateway::JsonValue::object();
        status["online"] = true;
        status["setpoint"] = tempController.getSetpoint();
        status["mode"] = "automatic";

        client.publish("status." + config.deviceId, status);
    });

    // Connect
    if (!client.connect()) {
        std::cerr << "Failed to connect" << std::endl;
        return 1;
    }

    // Subscribe to all sensor telemetry
    client.subscribe("telemetry.sensor-*.>",
        [&](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            // Extract sensor ID from subject (telemetry.sensor-xxx.metric)
            size_t start = subject.find("sensor-");
            if (start == std::string::npos) return;

            size_t end = subject.find('.', start);
            std::string sensorId = subject.substr(start, end - start);

            // Extract metric type
            size_t metricStart = subject.rfind('.') + 1;
            std::string metric = subject.substr(metricStart);

            // Update aggregator
            if (payload.contains("temperature")) {
                double temp = payload["temperature"].asDouble();
                sensors.update(sensorId, "temperature", temp);
                std::cout << "[TELEMETRY] " << sensorId << " temperature: " << temp << "C" << std::endl;
            }

            if (payload.contains("humidity")) {
                double humidity = payload["humidity"].asDouble();
                sensors.update(sensorId, "humidity", humidity);
            }
        });

    // Subscribe to actuator status updates
    client.subscribe("status.actuator-*",
        [&](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            size_t start = subject.find("actuator-");
            if (start == std::string::npos) return;

            std::string actuatorId = subject.substr(start);

            if (payload.contains("state")) {
                std::lock_guard<std::mutex> lock(actuatorMutex);
                actuatorStates[actuatorId] = payload["state"].asString();
                std::cout << "[STATUS] " << actuatorId << " state: "
                         << actuatorStates[actuatorId] << std::endl;
            }
        });

    // Subscribe to configuration updates
    client.subscribe("config." + config.deviceId + ".>",
        [&](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            std::cout << "[CONFIG] Update received" << std::endl;

            if (payload.contains("setpoint")) {
                double newSetpoint = payload["setpoint"].asDouble();
                tempController.setSetpoint(newSetpoint);
                std::cout << "  -> New setpoint: " << newSetpoint << "C" << std::endl;
            }
        });

    // Subscribe to operator commands
    client.subscribe("commands." + config.deviceId + ".>",
        [&](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message&) {
            std::cout << "[COMMAND] " << subject << std::endl;

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "emergency_stop") {
                    std::cout << "  -> EMERGENCY STOP - Sending to all actuators" << std::endl;

                    gateway::JsonValue cmd = gateway::JsonValue::object();
                    cmd["action"] = "emergency_stop";

                    // Send to all known actuators
                    client.publish("commands.actuator-valve-001", cmd);
                }
            }
        });

    std::cout << "Controller ready. Monitoring sensors..." << std::endl;

    // Control loop interval
    const auto controlInterval = std::chrono::seconds(5);
    auto lastControl = std::chrono::steady_clock::now();

    // Status reporting interval
    const auto statusInterval = std::chrono::seconds(30);
    auto lastStatus = std::chrono::steady_clock::now();

    while (running) {
        client.poll(gateway::Duration{100});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        auto now = std::chrono::steady_clock::now();

        // Run control loop
        if (now - lastControl >= controlInterval) {
            lastControl = now;

            // Get average temperature from all sensors
            double avgTemp = sensors.getAverage("temperature", 25.0);

            // Evaluate control action
            auto action = tempController.evaluate(avgTemp);

            std::cout << "[CONTROL] Avg temp: " << avgTemp << "C, Action: "
                     << TemperatureController::actionToString(action) << std::endl;

            // Send commands to actuators based on action
            if (action == TemperatureController::Action::Cool) {
                // Open cooling valve
                gateway::JsonValue cmd = gateway::JsonValue::object();
                cmd["action"] = "open";
                cmd["position"] = 75.0;

                client.publish("commands.actuator-valve-001", cmd);
            } else if (action == TemperatureController::Action::Heat) {
                // Close cooling valve (or open heating)
                gateway::JsonValue cmd = gateway::JsonValue::object();
                cmd["action"] = "close";

                client.publish("commands.actuator-valve-001", cmd);
            }

            // Publish control decision
            gateway::JsonValue decision = gateway::JsonValue::object();
            decision["average_temperature"] = avgTemp;
            decision["setpoint"] = tempController.getSetpoint();
            decision["action"] = TemperatureController::actionToString(action);
            decision["sensor_count"] = static_cast<int64_t>(sensors.getSensorIds().size());

            client.publish("decisions." + config.deviceId, decision);
        }

        // Periodic status report
        if (now - lastStatus >= statusInterval) {
            lastStatus = now;

            gateway::JsonValue status = gateway::JsonValue::object();
            status["online"] = true;
            status["setpoint"] = tempController.getSetpoint();
            status["average_temperature"] = sensors.getAverage("temperature", 0.0);
            status["sensor_count"] = static_cast<int64_t>(sensors.getSensorIds().size());

            gateway::JsonValue actuators = gateway::JsonValue::object();
            {
                std::lock_guard<std::mutex> lock(actuatorMutex);
                for (const auto& [id, state] : actuatorStates) {
                    actuators[id] = state;
                }
            }
            status["actuators"] = actuators;

            client.publish("status." + config.deviceId, status);

            std::cout << "[STATUS] Published controller status" << std::endl;
        }
    }

    // Publish offline status
    gateway::JsonValue offline = gateway::JsonValue::object();
    offline["online"] = false;
    client.publish("status." + config.deviceId, offline);

    // Allow message to be sent
    client.poll(gateway::Duration{200});

    client.disconnect();
    std::cout << "Controller shutdown complete" << std::endl;

    return 0;
}
