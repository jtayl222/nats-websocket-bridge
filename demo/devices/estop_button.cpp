/**
 * @file estop_button.cpp
 * @brief Emergency stop button simulator for packaging line demo
 *
 * Simulates a physical E-Stop button with interactive triggering.
 * Demonstrates fan-out broadcast pattern to all subsystems.
 *
 * Demo features:
 * - Interactive triggering via stdin
 * - Broadcast to all line devices
 * - Latching behavior (requires reset)
 * - Safety audit logging
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <atomic>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "sensor-estop-001";
const std::string TOKEN = "estop-token-001";
const std::string ESTOP_SUBJECT = "factory.line1.eStop";
const std::string EMERGENCY_BROADCAST = "factory.line1.emergency";
const std::string ALERTS_SUBJECT = "factory.line1.alerts.emergency";

class EStopButton {
public:
    enum class State { Ready, Triggered, Reset };

    EStopButton() : state_(State::Ready), triggerCount_(0) {}

    bool trigger(const std::string& reason = "Manual activation") {
        if (state_ == State::Triggered) {
            return false;  // Already triggered
        }
        state_ = State::Triggered;
        triggerCount_++;
        lastReason_ = reason;
        triggeredAt_ = std::chrono::system_clock::now();
        return true;
    }

    bool reset() {
        if (state_ != State::Triggered) {
            return false;
        }
        state_ = State::Ready;
        return true;
    }

    State getState() const { return state_; }
    int getTriggerCount() const { return triggerCount_; }
    const std::string& getLastReason() const { return lastReason_; }

    static const char* stateToString(State state) {
        switch (state) {
            case State::Ready: return "ready";
            case State::Triggered: return "triggered";
            case State::Reset: return "reset";
            default: return "unknown";
        }
    }

private:
    std::atomic<State> state_;
    int triggerCount_;
    std::string lastReason_;
    std::chrono::system_clock::time_point triggeredAt_;
};

// Thread-safe input handler
std::atomic<bool> triggerRequested{false};
std::atomic<bool> resetRequested{false};

void inputThread() {
    std::cout << "\n";
    std::cout << color::YELLOW << "╔══════════════════════════════════════╗\n";
    std::cout << "║  Press ENTER to trigger E-Stop       ║\n";
    std::cout << "║  Type 'reset' + ENTER to reset       ║\n";
    std::cout << "║  Type 'quit' + ENTER to exit         ║\n";
    std::cout << "╚══════════════════════════════════════╝" << color::RESET << "\n\n";

    std::string input;
    while (g_running) {
        if (std::getline(std::cin, input)) {
            if (input == "quit" || input == "exit") {
                g_running = false;
                break;
            } else if (input == "reset") {
                resetRequested = true;
            } else {
                triggerRequested = true;
            }
        }
    }
}

int main() {
    installSignalHandlers();
    printBanner("EMERGENCY STOP BUTTON");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Sensor);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("E-Stop subject: " + ESTOP_SUBJECT);
    printStatus("Broadcast subject: " + EMERGENCY_BROADCAST);

    // Create client and button
    GatewayClient client(config);
    EStopButton button;

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");

        // Publish initial status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["state"] = EStopButton::stateToString(button.getState());
        status["triggerCount"] = button.getTriggerCount();

        client.publish("factory.line1.status." + DEVICE_ID, status);
    });

    client.onDisconnected([](ErrorCode code, const std::string& reason) {
        printWarning("Disconnected: " + reason);
    });

    // Connect
    printStatus("Connecting to gateway...");
    if (!client.connect()) {
        printError("Failed to connect to gateway!");
        return 1;
    }

    // Subscribe to commands (for remote reset)
    client.subscribe("factory.line1.cmd." + DEVICE_ID + ".>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printReceive(subject, "Command received");

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "reset") {
                    resetRequested = true;
                } else if (action == "test") {
                    // Test mode - trigger and auto-reset
                    printWarning("E-STOP TEST triggered");
                    // Just publish test message, don't actually trigger
                    JsonValue test = JsonValue::object();
                    test["type"] = "test";
                    test["device"] = DEVICE_ID;
                    test["timestamp"] = getTimestamp();

                    client.publish(ESTOP_SUBJECT + ".test", test);
                }
            }
        });

    // Start input thread
    std::thread input(inputThread);

    printStatus("E-Stop button ready.");

    // Main loop
    while (g_running) {
        client.poll(Duration{100});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        // Check for trigger request
        if (triggerRequested.exchange(false)) {
            if (button.trigger("Manual activation")) {
                std::cout << "\n";
                printAlert("EMERGENCY", "🛑 E-STOP TRIGGERED!");
                std::cout << "\n";

                // Publish E-Stop event
                JsonValue estop = JsonValue::object();
                estop["triggered"] = true;
                estop["device"] = DEVICE_ID;
                estop["reason"] = "Manual activation";
                estop["triggerCount"] = button.getTriggerCount();
                estop["timestamp"] = getTimestamp();
                estop["batch"] = demoConfig.batchId;

                client.publish(ESTOP_SUBJECT, estop);
                printPublish(ESTOP_SUBJECT, "E-STOP TRIGGERED");

                // Broadcast emergency to all subsystems
                JsonValue emergency = JsonValue::object();
                emergency["type"] = "emergency_stop";
                emergency["source"] = DEVICE_ID;
                emergency["action"] = "STOP_ALL";
                emergency["reason"] = "E-Stop button activated";
                emergency["timestamp"] = getTimestamp();

                client.publish(EMERGENCY_BROADCAST, emergency);
                printPublish(EMERGENCY_BROADCAST, "Emergency broadcast sent to all devices");

                // Publish alert
                JsonValue alert = JsonValue::object();
                alert["severity"] = "emergency";
                alert["type"] = "estop_activated";
                alert["device"] = DEVICE_ID;
                alert["message"] = "Emergency stop button activated!";
                alert["timestamp"] = getTimestamp();

                client.publish(ALERTS_SUBJECT, alert);

                // Publish status
                JsonValue status = JsonValue::object();
                status["state"] = "triggered";
                status["triggerCount"] = button.getTriggerCount();
                status["timestamp"] = getTimestamp();

                client.publish("factory.line1.status." + DEVICE_ID, status);

                std::cout << color::RED << "\n  *** LINE STOPPED - Type 'reset' to clear ***\n" << color::RESET << std::endl;

            } else {
                printWarning("E-Stop already triggered - reset required");
            }
        }

        // Check for reset request
        if (resetRequested.exchange(false)) {
            if (button.reset()) {
                std::cout << "\n";
                printStatus("✓ E-Stop RESET - Line can resume");
                std::cout << "\n";

                // Publish reset event
                JsonValue reset = JsonValue::object();
                reset["triggered"] = false;
                reset["device"] = DEVICE_ID;
                reset["action"] = "reset";
                reset["timestamp"] = getTimestamp();

                client.publish(ESTOP_SUBJECT, reset);
                printPublish(ESTOP_SUBJECT, "E-STOP RESET");

                // Publish clear broadcast
                JsonValue clear = JsonValue::object();
                clear["type"] = "emergency_clear";
                clear["source"] = DEVICE_ID;
                clear["action"] = "RESUME_ALLOWED";
                clear["timestamp"] = getTimestamp();

                client.publish(EMERGENCY_BROADCAST, clear);
                printPublish(EMERGENCY_BROADCAST, "Emergency cleared - resume allowed");

                // Publish status
                JsonValue status = JsonValue::object();
                status["state"] = "ready";
                status["triggerCount"] = button.getTriggerCount();
                status["timestamp"] = getTimestamp();

                client.publish("factory.line1.status." + DEVICE_ID, status);

            } else {
                printWarning("E-Stop not triggered - nothing to reset");
            }
        }
    }

    // Cleanup
    if (input.joinable()) {
        input.detach();  // Let it exit naturally
    }

    // Publish offline
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["state"] = EStopButton::stateToString(button.getState());
    offline["triggerCount"] = button.getTriggerCount();
    offline["timestamp"] = getTimestamp();

    client.publish("factory.line1.status." + DEVICE_ID, offline);
    client.poll(Duration{200});

    client.disconnect();

    printStatus("E-Stop button shutdown complete.");
    printStatus("Total triggers: " + std::to_string(button.getTriggerCount()));

    return 0;
}
