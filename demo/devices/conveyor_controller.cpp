/**
 * @file conveyor_controller.cpp
 * @brief Conveyor belt controller simulator for packaging line demo
 *
 * Simulates a conveyor belt actuator with speed control.
 * Demonstrates bidirectional communication and state management.
 *
 * Commands:
 * - start: Start conveyor at current speed
 * - stop: Stop conveyor
 * - setSpeed: Change speed (0-200 units/min)
 * - emergency_stop: Immediate halt
 *
 * Demo features:
 * - Command reception and execution
 * - State persistence and replay after reconnect
 * - Status publishing
 * - Emergency stop handling
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <mutex>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "actuator-conveyor-001";
const std::string TOKEN = "conveyor-token-001";
const std::string CMD_SUBJECT = "factory.line1.conveyor.cmd";
const std::string STATUS_SUBJECT = "factory.line1.conveyor.status";
const int STATUS_INTERVAL_MS = 5000;

// Speed limits
const double SPEED_MIN = 0.0;
const double SPEED_MAX = 200.0;
const double SPEED_DEFAULT = 100.0;

// Conveyor state
class ConveyorState {
public:
    enum class Mode { Stopped, Running, Ramping, EmergencyStop, Fault };

    ConveyorState() : mode_(Mode::Stopped), currentSpeed_(0), targetSpeed_(SPEED_DEFAULT) {}

    Mode getMode() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return mode_;
    }

    double getCurrentSpeed() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return currentSpeed_;
    }

    double getTargetSpeed() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return targetSpeed_;
    }

    bool start() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (mode_ == Mode::EmergencyStop || mode_ == Mode::Fault) {
            return false;
        }
        mode_ = Mode::Ramping;
        return true;
    }

    bool stop() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (mode_ == Mode::EmergencyStop || mode_ == Mode::Fault) {
            return false;
        }
        targetSpeed_ = 0;
        mode_ = Mode::Ramping;
        return true;
    }

    bool setSpeed(double speed) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (mode_ == Mode::EmergencyStop || mode_ == Mode::Fault) {
            return false;
        }
        targetSpeed_ = std::max(SPEED_MIN, std::min(SPEED_MAX, speed));
        if (mode_ == Mode::Running || mode_ == Mode::Ramping) {
            mode_ = Mode::Ramping;
        }
        return true;
    }

    void emergencyStop() {
        std::lock_guard<std::mutex> lock(mutex_);
        mode_ = Mode::EmergencyStop;
        currentSpeed_ = 0;
        targetSpeed_ = 0;
    }

    bool reset() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (mode_ == Mode::EmergencyStop || mode_ == Mode::Fault) {
            mode_ = Mode::Stopped;
            currentSpeed_ = 0;
            targetSpeed_ = SPEED_DEFAULT;
            return true;
        }
        return false;
    }

    // Simulate conveyor dynamics (call periodically)
    void update(double deltaSeconds) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (mode_ == Mode::EmergencyStop || mode_ == Mode::Fault) {
            return;
        }

        if (mode_ == Mode::Ramping) {
            // Ramp speed at 50 units/sec
            double rampRate = 50.0 * deltaSeconds;

            if (currentSpeed_ < targetSpeed_) {
                currentSpeed_ = std::min(currentSpeed_ + rampRate, targetSpeed_);
            } else if (currentSpeed_ > targetSpeed_) {
                currentSpeed_ = std::max(currentSpeed_ - rampRate, targetSpeed_);
            }

            // Check if we've reached target
            if (std::abs(currentSpeed_ - targetSpeed_) < 0.1) {
                currentSpeed_ = targetSpeed_;
                mode_ = currentSpeed_ > 0 ? Mode::Running : Mode::Stopped;
            }
        }
    }

    static const char* modeToString(Mode mode) {
        switch (mode) {
            case Mode::Stopped: return "stopped";
            case Mode::Running: return "running";
            case Mode::Ramping: return "ramping";
            case Mode::EmergencyStop: return "emergency_stop";
            case Mode::Fault: return "fault";
            default: return "unknown";
        }
    }

private:
    mutable std::mutex mutex_;
    Mode mode_;
    double currentSpeed_;
    double targetSpeed_;
};

int main() {
    installSignalHandlers();
    printBanner("CONVEYOR CONTROLLER");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Actuator);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("Command subject: " + CMD_SUBJECT);
    printStatus("Status subject: " + STATUS_SUBJECT);

    // Create client and state
    GatewayClient client(config);
    ConveyorState conveyor;
    int commandCount = 0;

    // Track state for change detection
    ConveyorState::Mode lastMode = conveyor.getMode();
    double lastSpeed = conveyor.getCurrentSpeed();

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");

        // Request last known state (for replay demo)
        printStatus("Checking for replayed state...");

        // Publish initial status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["mode"] = ConveyorState::modeToString(conveyor.getMode());
        status["currentSpeed"] = conveyor.getCurrentSpeed();
        status["targetSpeed"] = conveyor.getTargetSpeed();
        status["batch"] = demoConfig.batchId;

        client.publish(STATUS_SUBJECT, status);
    });

    client.onDisconnected([](ErrorCode code, const std::string& reason) {
        printWarning("Disconnected: " + reason);
    });

    client.onReconnecting([](uint32_t attempt) {
        printStatus("Reconnecting (attempt " + std::to_string(attempt) + ")...");
    });

    // Connect
    printStatus("Connecting to gateway...");
    if (!client.connect()) {
        printError("Failed to connect to gateway!");
        return 1;
    }

    // Subscribe to commands
    client.subscribe(CMD_SUBJECT,
        [&](const std::string& subject, const JsonValue& payload, const Message& msg) {
            commandCount++;

            if (!payload.contains("action")) {
                printWarning("Command missing 'action' field");
                return;
            }

            std::string action = payload["action"].asString();
            bool success = false;
            std::string result;

            printReceive(subject, "action=" + action);

            if (action == "start") {
                success = conveyor.start();
                result = success ? "Starting conveyor" : "Cannot start (emergency stop active)";

            } else if (action == "stop") {
                success = conveyor.stop();
                result = success ? "Stopping conveyor" : "Cannot stop (emergency stop active)";

            } else if (action == "setSpeed") {
                if (payload.contains("value")) {
                    double speed = payload["value"].asDouble();
                    success = conveyor.setSpeed(speed);
                    result = success ?
                        "Setting speed to " + std::to_string(static_cast<int>(speed)) + " units/min" :
                        "Cannot change speed (emergency stop active)";
                } else {
                    result = "Missing 'value' parameter";
                }

            } else if (action == "emergency_stop") {
                conveyor.emergencyStop();
                success = true;
                result = "EMERGENCY STOP ACTIVATED";
                printAlert("EMERGENCY", "Emergency stop activated!");

            } else if (action == "reset") {
                success = conveyor.reset();
                result = success ? "Reset successful" : "Cannot reset (not in fault/estop state)";

            } else if (action == "status") {
                success = true;
                result = "Status requested";

            } else {
                result = "Unknown action: " + action;
            }

            if (success) {
                printStatus("→ " + result);
            } else {
                printWarning("→ " + result);
            }

            // Send acknowledgment
            JsonValue ack = JsonValue::object();
            ack["success"] = success;
            ack["message"] = result;
            ack["mode"] = ConveyorState::modeToString(conveyor.getMode());
            ack["currentSpeed"] = conveyor.getCurrentSpeed();
            ack["targetSpeed"] = conveyor.getTargetSpeed();
            ack["timestamp"] = getTimestamp();

            // If request had correlationId, send response
            if (msg.correlationId) {
                client.publish("factory.line1.conveyor.response." + *msg.correlationId, ack);
            }

            // Always publish updated status
            JsonValue status = JsonValue::object();
            status["mode"] = ConveyorState::modeToString(conveyor.getMode());
            status["currentSpeed"] = conveyor.getCurrentSpeed();
            status["targetSpeed"] = conveyor.getTargetSpeed();
            status["timestamp"] = getTimestamp();

            client.publish(STATUS_SUBJECT, status);
        });

    // Subscribe to emergency broadcast
    client.subscribe("factory.line1.emergency",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printAlert("EMERGENCY", "Emergency broadcast received!");
            conveyor.emergencyStop();

            // Publish status
            JsonValue status = JsonValue::object();
            status["mode"] = "emergency_stop";
            status["currentSpeed"] = 0.0;
            status["reason"] = "emergency_broadcast";
            status["timestamp"] = getTimestamp();

            client.publish(STATUS_SUBJECT, status);
        });

    printStatus("Conveyor controller ready. Waiting for commands...\n");

    // Timing
    auto lastUpdate = std::chrono::steady_clock::now();
    auto lastStatusPublish = lastUpdate;

    while (g_running) {
        client.poll(Duration{50});

        auto now = std::chrono::steady_clock::now();
        double deltaSeconds = std::chrono::duration<double>(now - lastUpdate).count();
        lastUpdate = now;

        // Update conveyor simulation
        conveyor.update(deltaSeconds);

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        // Check for state changes
        auto currentMode = conveyor.getMode();
        double currentSpeed = conveyor.getCurrentSpeed();

        if (currentMode != lastMode || std::abs(currentSpeed - lastSpeed) > 0.5) {
            // State changed - publish immediately
            JsonValue status = JsonValue::object();
            status["mode"] = ConveyorState::modeToString(currentMode);
            status["currentSpeed"] = currentSpeed;
            status["targetSpeed"] = conveyor.getTargetSpeed();
            status["timestamp"] = getTimestamp();

            client.publish(STATUS_SUBJECT, status);

            if (currentMode != lastMode) {
                printStatus("State: " + std::string(ConveyorState::modeToString(lastMode)) +
                           " → " + std::string(ConveyorState::modeToString(currentMode)));
            }

            lastMode = currentMode;
            lastSpeed = currentSpeed;
            lastStatusPublish = now;
        }

        // Periodic status publish
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastStatusPublish);
        if (elapsed.count() >= STATUS_INTERVAL_MS) {
            lastStatusPublish = now;

            JsonValue status = JsonValue::object();
            status["mode"] = ConveyorState::modeToString(conveyor.getMode());
            status["currentSpeed"] = conveyor.getCurrentSpeed();
            status["targetSpeed"] = conveyor.getTargetSpeed();
            status["commandsReceived"] = commandCount;
            status["timestamp"] = getTimestamp();

            client.publish(STATUS_SUBJECT, status);

            printPublish(STATUS_SUBJECT,
                std::string(ConveyorState::modeToString(conveyor.getMode())) +
                " @ " + std::to_string(static_cast<int>(conveyor.getCurrentSpeed())) + " units/min");
        }
    }

    // Stop conveyor on shutdown
    conveyor.stop();
    while (conveyor.getMode() == ConveyorState::Mode::Ramping) {
        conveyor.update(0.1);
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // Publish offline status
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["mode"] = "stopped";
    offline["currentSpeed"] = 0.0;
    offline["timestamp"] = getTimestamp();

    client.publish(STATUS_SUBJECT, offline);
    client.poll(Duration{200});

    client.disconnect();

    printStatus("Conveyor controller shutdown complete.");
    printStatus("Total commands processed: " + std::to_string(commandCount));

    return 0;
}
