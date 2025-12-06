/**
 * @file actuator.cpp
 * @brief Industrial actuator (valve) example
 *
 * Demonstrates:
 * - Bidirectional communication
 * - Command handling with acknowledgment
 * - State management
 * - Status reporting
 */

#include <gateway/gateway_device.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <csignal>
#include <atomic>
#include <mutex>

std::atomic<bool> running{true};

void signalHandler(int) {
    running = false;
}

// Simulated valve actuator
class ValveActuator {
public:
    enum class State { Closed, Opening, Open, Closing, Fault };

    ValveActuator() : state_(State::Closed), position_(0.0) {}

    State getState() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return state_;
    }

    double getPosition() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return position_;
    }

    bool open(double targetPosition = 100.0) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ == State::Fault) return false;

        targetPosition_ = std::max(0.0, std::min(100.0, targetPosition));
        state_ = State::Opening;
        return true;
    }

    bool close() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ == State::Fault) return false;

        targetPosition_ = 0.0;
        state_ = State::Closing;
        return true;
    }

    bool setPosition(double position) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ == State::Fault) return false;

        targetPosition_ = std::max(0.0, std::min(100.0, position));
        state_ = position > position_ ? State::Opening : State::Closing;
        return true;
    }

    bool emergencyStop() {
        std::lock_guard<std::mutex> lock(mutex_);
        state_ = State::Fault;
        return true;
    }

    bool reset() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (state_ == State::Fault) {
            state_ = State::Closed;
            position_ = 0.0;
            targetPosition_ = 0.0;
            return true;
        }
        return false;
    }

    // Simulate valve movement (call periodically)
    void update() {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ == State::Opening) {
            position_ += 5.0;  // 5% per update
            if (position_ >= targetPosition_) {
                position_ = targetPosition_;
                state_ = position_ >= 99.0 ? State::Open : State::Closed;
            }
        } else if (state_ == State::Closing) {
            position_ -= 5.0;
            if (position_ <= targetPosition_) {
                position_ = targetPosition_;
                state_ = position_ <= 1.0 ? State::Closed : State::Open;
            }
        }

        position_ = std::max(0.0, std::min(100.0, position_));
    }

    static const char* stateToString(State state) {
        switch (state) {
            case State::Closed: return "closed";
            case State::Opening: return "opening";
            case State::Open: return "open";
            case State::Closing: return "closing";
            case State::Fault: return "fault";
            default: return "unknown";
        }
    }

private:
    mutable std::mutex mutex_;
    State state_;
    double position_;
    double targetPosition_ = 0.0;
};

int main() {
    std::signal(SIGINT, signalHandler);
    std::signal(SIGTERM, signalHandler);

    std::cout << "=== Valve Actuator Example ===" << std::endl;

    // Configuration
    gateway::GatewayConfig config;
    config.gatewayUrl = "wss://localhost:5000/ws";
    config.deviceId = "actuator-valve-001";
    config.authToken = "actuator-token-001";
    config.deviceType = gateway::DeviceType::Actuator;

    // Create client and actuator
    gateway::GatewayClient client(config);
    ValveActuator valve;

    // Track last reported state for change detection
    ValveActuator::State lastReportedState = ValveActuator::State::Closed;
    double lastReportedPosition = 0.0;

    // Connection callbacks
    client.onConnected([&] {
        std::cout << "Connected! Reporting initial state..." << std::endl;

        // Report initial state
        gateway::JsonValue status = gateway::JsonValue::object();
        status["state"] = ValveActuator::stateToString(valve.getState());
        status["position"] = valve.getPosition();
        status["online"] = true;

        client.publish("status." + config.deviceId, status);
    });

    client.onDisconnected([](gateway::ErrorCode, const std::string& reason) {
        std::cout << "Disconnected: " << reason << std::endl;
    });

    // Connect
    if (!client.connect()) {
        std::cerr << "Failed to connect" << std::endl;
        return 1;
    }

    // Subscribe to commands
    client.subscribe("commands." + config.deviceId + ".>",
        [&](const std::string& subject, const gateway::JsonValue& payload, const gateway::Message& msg) {
            std::cout << "Command received: " << subject << std::endl;

            std::string action;
            if (payload.contains("action")) {
                action = payload["action"].asString();
            }

            bool success = false;
            std::string result;

            // Handle commands
            if (action == "open") {
                double position = 100.0;
                if (payload.contains("position")) {
                    position = payload["position"].asDouble();
                }
                success = valve.open(position);
                result = success ? "Opening valve" : "Failed to open (fault state)";

            } else if (action == "close") {
                success = valve.close();
                result = success ? "Closing valve" : "Failed to close (fault state)";

            } else if (action == "set_position") {
                if (payload.contains("position")) {
                    double pos = payload["position"].asDouble();
                    success = valve.setPosition(pos);
                    result = success ? "Setting position to " + std::to_string(pos) + "%" : "Failed (fault state)";
                } else {
                    result = "Missing position parameter";
                }

            } else if (action == "emergency_stop") {
                success = valve.emergencyStop();
                result = "Emergency stop activated";

            } else if (action == "reset") {
                success = valve.reset();
                result = success ? "Reset successful" : "Reset failed (not in fault state)";

            } else if (action == "status") {
                success = true;
                result = "Status requested";

            } else {
                result = "Unknown action: " + action;
            }

            std::cout << "  -> " << result << std::endl;

            // Send acknowledgment
            gateway::JsonValue ack = gateway::JsonValue::object();
            ack["success"] = success;
            ack["message"] = result;
            ack["state"] = ValveActuator::stateToString(valve.getState());
            ack["position"] = valve.getPosition();

            if (msg.correlationId) {
                // Reply to specific request
                client.publish("responses." + config.deviceId + "." + msg.correlationId.value(), ack);
            }

            // Always publish current status
            gateway::JsonValue status = gateway::JsonValue::object();
            status["state"] = ValveActuator::stateToString(valve.getState());
            status["position"] = valve.getPosition();

            client.publish("status." + config.deviceId, status);
        });

    std::cout << "Actuator ready. Waiting for commands..." << std::endl;

    // Status reporting interval
    const auto statusInterval = std::chrono::seconds(10);
    auto lastStatus = std::chrono::steady_clock::now();

    // Valve update interval
    const auto updateInterval = std::chrono::milliseconds(200);
    auto lastUpdate = std::chrono::steady_clock::now();

    while (running) {
        client.poll(gateway::Duration{50});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        auto now = std::chrono::steady_clock::now();

        // Update valve simulation
        if (now - lastUpdate >= updateInterval) {
            lastUpdate = now;
            valve.update();

            // Report state changes immediately
            auto currentState = valve.getState();
            double currentPosition = valve.getPosition();

            if (currentState != lastReportedState ||
                std::abs(currentPosition - lastReportedPosition) > 1.0) {

                gateway::JsonValue status = gateway::JsonValue::object();
                status["state"] = ValveActuator::stateToString(currentState);
                status["position"] = currentPosition;

                client.publish("status." + config.deviceId, status);

                if (currentState != lastReportedState) {
                    std::cout << "State change: " << ValveActuator::stateToString(currentState)
                             << " (position: " << currentPosition << "%)" << std::endl;
                }

                lastReportedState = currentState;
                lastReportedPosition = currentPosition;
            }
        }

        // Periodic status report
        if (now - lastStatus >= statusInterval) {
            lastStatus = now;

            gateway::JsonValue heartbeat = gateway::JsonValue::object();
            heartbeat["state"] = ValveActuator::stateToString(valve.getState());
            heartbeat["position"] = valve.getPosition();
            heartbeat["online"] = true;
            heartbeat["uptime_ms"] = static_cast<int64_t>(
                std::chrono::duration_cast<std::chrono::milliseconds>(
                    now.time_since_epoch()).count());

            client.publish("heartbeat." + config.deviceId, heartbeat);
        }
    }

    // Report offline status before disconnecting
    gateway::JsonValue offline = gateway::JsonValue::object();
    offline["state"] = ValveActuator::stateToString(valve.getState());
    offline["position"] = valve.getPosition();
    offline["online"] = false;

    client.publish("status." + config.deviceId, offline);

    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    client.poll(gateway::Duration{100});

    client.disconnect();
    std::cout << "Actuator shutdown complete" << std::endl;

    return 0;
}
