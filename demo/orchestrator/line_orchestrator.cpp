/**
 * @file line_orchestrator.cpp
 * @brief Line orchestrator/PLC simulator for packaging line demo
 *
 * Central controller that coordinates all devices on the packaging line.
 * Aggregates data, makes control decisions, and calculates OEE.
 *
 * Demo features:
 * - Aggregates status from all devices
 * - Coordinates start/stop sequences
 * - Calculates OEE (Overall Equipment Effectiveness)
 * - Handles emergency situations
 * - Provides unified line status
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <map>
#include <mutex>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "controller-orchestrator-001";
const std::string TOKEN = "orchestrator-token-001";
const std::string LINE_STATUS_SUBJECT = "factory.line1.status";
const std::string OEE_SUBJECT = "factory.line1.oee";
const int STATUS_INTERVAL_MS = 10000;

// Device status tracking
struct DeviceStatus {
    bool online = false;
    std::string state;
    std::chrono::steady_clock::time_point lastUpdate;
    JsonValue lastData;
};

// Line state
enum class LineState {
    Unknown,
    Stopped,
    Starting,
    Running,
    Stopping,
    Emergency,
    Fault
};

const char* lineStateToString(LineState state) {
    switch (state) {
        case LineState::Unknown: return "unknown";
        case LineState::Stopped: return "stopped";
        case LineState::Starting: return "starting";
        case LineState::Running: return "running";
        case LineState::Stopping: return "stopping";
        case LineState::Emergency: return "emergency";
        case LineState::Fault: return "fault";
        default: return "unknown";
    }
}

// OEE Calculator
class OEECalculator {
public:
    OEECalculator() : plannedTime_(28800), downtime_(0), idealCycleTime_(0.5) {}

    void updateProduction(int goodCount, int totalCount, double runtimeSeconds) {
        goodCount_ = goodCount;
        totalCount_ = totalCount;
        actualRuntime_ = runtimeSeconds;
    }

    void addDowntime(double seconds) {
        downtime_ += seconds;
    }

    void reset() {
        goodCount_ = 0;
        totalCount_ = 0;
        downtime_ = 0;
        actualRuntime_ = 0;
    }

    // Availability = (Planned Time - Downtime) / Planned Time
    double getAvailability() const {
        if (plannedTime_ <= 0) return 0;
        return std::max(0.0, (plannedTime_ - downtime_) / plannedTime_);
    }

    // Performance = (Ideal Cycle Time * Total Count) / Actual Runtime
    double getPerformance() const {
        if (actualRuntime_ <= 0) return 0;
        double idealTime = idealCycleTime_ * totalCount_;
        return std::min(1.0, idealTime / actualRuntime_);
    }

    // Quality = Good Count / Total Count
    double getQuality() const {
        if (totalCount_ <= 0) return 1.0;
        return static_cast<double>(goodCount_) / totalCount_;
    }

    // OEE = Availability * Performance * Quality
    double getOEE() const {
        return getAvailability() * getPerformance() * getQuality();
    }

    JsonValue toJson() const {
        JsonValue oee = JsonValue::object();
        oee["availability"] = getAvailability() * 100.0;
        oee["performance"] = getPerformance() * 100.0;
        oee["quality"] = getQuality() * 100.0;
        oee["oee"] = getOEE() * 100.0;
        oee["goodCount"] = goodCount_;
        oee["totalCount"] = totalCount_;
        oee["downtime"] = downtime_;
        oee["runtime"] = actualRuntime_;
        return oee;
    }

private:
    double plannedTime_;   // seconds
    double downtime_;      // seconds
    double idealCycleTime_; // seconds per item
    int goodCount_ = 0;
    int totalCount_ = 0;
    double actualRuntime_ = 0;
};

class LineOrchestrator {
public:
    LineOrchestrator() : state_(LineState::Stopped) {}

    void updateDeviceStatus(const std::string& deviceId, const JsonValue& status) {
        std::lock_guard<std::mutex> lock(mutex_);

        DeviceStatus& dev = devices_[deviceId];
        dev.online = status.contains("online") ? status["online"].asBool() : true;
        if (status.contains("state")) {
            dev.state = status["state"].asString();
        } else if (status.contains("mode")) {
            dev.state = status["mode"].asString();
        }
        dev.lastUpdate = std::chrono::steady_clock::now();
        dev.lastData = status;
    }

    void setLineState(LineState state) {
        std::lock_guard<std::mutex> lock(mutex_);
        state_ = state;
    }

    LineState getLineState() const {
        return state_;
    }

    bool allDevicesOnline() const {
        std::lock_guard<std::mutex> lock(mutex_);

        for (const auto& [id, dev] : devices_) {
            if (!dev.online) return false;

            // Check for stale status (>30s)
            auto age = std::chrono::steady_clock::now() - dev.lastUpdate;
            if (age > std::chrono::seconds(30)) return false;
        }

        return !devices_.empty();
    }

    bool isConveyorRunning() const {
        std::lock_guard<std::mutex> lock(mutex_);

        auto it = devices_.find("actuator-conveyor-001");
        if (it == devices_.end()) return false;

        return it->second.state == "running";
    }

    int getOnlineDeviceCount() const {
        std::lock_guard<std::mutex> lock(mutex_);

        int count = 0;
        for (const auto& [id, dev] : devices_) {
            if (dev.online) count++;
        }
        return count;
    }

    JsonValue getStatusSummary() const {
        std::lock_guard<std::mutex> lock(mutex_);

        JsonValue summary = JsonValue::object();
        summary["lineState"] = lineStateToString(state_);
        summary["deviceCount"] = static_cast<int64_t>(devices_.size());
        summary["onlineCount"] = static_cast<int64_t>(getOnlineDeviceCount());

        JsonValue devList = JsonValue::object();
        for (const auto& [id, dev] : devices_) {
            JsonValue devStatus = JsonValue::object();
            devStatus["online"] = dev.online;
            devStatus["state"] = dev.state;
            devList[id] = devStatus;
        }
        summary["devices"] = devList;

        return summary;
    }

    OEECalculator& getOEE() { return oee_; }

private:
    mutable std::mutex mutex_;
    LineState state_;
    std::map<std::string, DeviceStatus> devices_;
    OEECalculator oee_;
};

int main() {
    installSignalHandlers();
    printBanner("LINE ORCHESTRATOR");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Controller);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("Line: " + demoConfig.lineName);
    printStatus("Batch: " + demoConfig.batchId);

    // Create client and orchestrator
    GatewayClient client(config);
    LineOrchestrator orchestrator;

    auto startTime = std::chrono::steady_clock::now();
    bool emergencyActive = false;

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");
        printStatus("Orchestrator taking control of " + demoConfig.lineName);

        // Publish initial status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["lineId"] = demoConfig.lineId;
        status["lineName"] = demoConfig.lineName;
        status["batch"] = demoConfig.batchId;
        status["state"] = "initializing";

        client.publish(LINE_STATUS_SUBJECT + ".orchestrator", status);
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

    // Subscribe to all device status updates
    client.subscribe("factory.line1.status.>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            // Extract device ID from subject
            size_t lastDot = subject.rfind('.');
            if (lastDot != std::string::npos) {
                std::string deviceId = subject.substr(lastDot + 1);
                if (deviceId != "orchestrator") {  // Don't track ourselves
                    orchestrator.updateDeviceStatus(deviceId, payload);
                }
            }
        });

    // Subscribe to production output for OEE
    client.subscribe("factory.line1.output",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("count") && payload.contains("total")) {
                int good = static_cast<int>(payload["count"].asInt());
                int total = static_cast<int>(payload["total"].asInt());
                double runtime = payload.contains("runtimeSeconds") ?
                                 payload["runtimeSeconds"].asDouble() : 0;
                orchestrator.getOEE().updateProduction(good, total, runtime);
            }
        });

    // Subscribe to conveyor status
    client.subscribe("factory.line1.conveyor.status",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("mode")) {
                std::string mode = payload["mode"].asString();
                if (mode == "running") {
                    orchestrator.setLineState(LineState::Running);
                } else if (mode == "stopped") {
                    if (!emergencyActive) {
                        orchestrator.setLineState(LineState::Stopped);
                    }
                } else if (mode == "emergency_stop") {
                    orchestrator.setLineState(LineState::Emergency);
                }
            }
        });

    // Subscribe to emergency events
    client.subscribe("factory.line1.emergency",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("type")) {
                std::string type = payload["type"].asString();

                if (type == "emergency_stop") {
                    emergencyActive = true;
                    orchestrator.setLineState(LineState::Emergency);
                    printAlert("EMERGENCY", "Emergency stop - line halted!");

                } else if (type == "emergency_clear") {
                    emergencyActive = false;
                    orchestrator.setLineState(LineState::Stopped);
                    printStatus("Emergency cleared - line can resume");
                }
            }
        });

    // Subscribe to alerts for logging
    client.subscribe("factory.line1.alerts.>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("severity") && payload.contains("message")) {
                std::string severity = payload["severity"].asString();
                std::string message = payload["message"].asString();
                printAlert(severity, message);
            }
        });

    // Subscribe to commands
    client.subscribe("factory.line1.cmd.orchestrator.>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printReceive(subject, "Command received");

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "start_line") {
                    if (emergencyActive) {
                        printWarning("Cannot start - emergency stop active");
                    } else {
                        printStatus("Starting line...");
                        orchestrator.setLineState(LineState::Starting);

                        // Send start command to conveyor
                        JsonValue cmd = JsonValue::object();
                        cmd["action"] = "start";
                        client.publish("factory.line1.conveyor.cmd", cmd);
                    }

                } else if (action == "stop_line") {
                    printStatus("Stopping line...");
                    orchestrator.setLineState(LineState::Stopping);

                    // Send stop command to conveyor
                    JsonValue cmd = JsonValue::object();
                    cmd["action"] = "stop";
                    client.publish("factory.line1.conveyor.cmd", cmd);

                } else if (action == "set_speed") {
                    if (payload.contains("value")) {
                        double speed = payload["value"].asDouble();

                        JsonValue cmd = JsonValue::object();
                        cmd["action"] = "setSpeed";
                        cmd["value"] = speed;
                        client.publish("factory.line1.conveyor.cmd", cmd);

                        printStatus("Speed command sent: " + std::to_string(static_cast<int>(speed)));
                    }

                } else if (action == "status") {
                    // Publish full status
                    auto summary = orchestrator.getStatusSummary();
                    summary["oee"] = orchestrator.getOEE().toJson();
                    summary["timestamp"] = getTimestamp();

                    client.publish(LINE_STATUS_SUBJECT, summary);

                } else if (action == "reset_oee") {
                    orchestrator.getOEE().reset();
                    startTime = std::chrono::steady_clock::now();
                    printStatus("OEE statistics reset");
                }
            }
        });

    printStatus("Orchestrator ready. Monitoring line...\n");

    // Timing
    auto lastStatus = std::chrono::steady_clock::now();

    while (g_running) {
        client.poll(Duration{100});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastStatus);

        if (elapsed.count() >= STATUS_INTERVAL_MS) {
            lastStatus = now;

            // Build and publish line status
            auto summary = orchestrator.getStatusSummary();
            summary["batch"] = demoConfig.batchId;
            summary["lot"] = demoConfig.lotNumber;
            summary["timestamp"] = getTimestamp();

            auto runTime = std::chrono::duration_cast<std::chrono::seconds>(now - startTime);
            summary["uptimeSeconds"] = static_cast<int64_t>(runTime.count());

            client.publish(LINE_STATUS_SUBJECT, summary);

            // Publish OEE
            auto oeeData = orchestrator.getOEE().toJson();
            oeeData["timestamp"] = getTimestamp();
            oeeData["batch"] = demoConfig.batchId;

            client.publish(OEE_SUBJECT, oeeData);

            // Print summary
            std::ostringstream oss;
            oss << "Line: " << lineStateToString(orchestrator.getLineState());
            oss << " | Devices: " << orchestrator.getOnlineDeviceCount();
            oss << " | OEE: " << std::fixed << std::setprecision(1)
                << orchestrator.getOEE().getOEE() * 100.0 << "%";

            printPublish(LINE_STATUS_SUBJECT, oss.str());
        }
    }

    // Final OEE report
    printStatus("\n=== Final OEE Report ===");
    auto oee = orchestrator.getOEE();
    printStatus("Availability: " + std::to_string(oee.getAvailability() * 100.0) + "%");
    printStatus("Performance: " + std::to_string(oee.getPerformance() * 100.0) + "%");
    printStatus("Quality: " + std::to_string(oee.getQuality() * 100.0) + "%");
    printStatus("OEE: " + std::to_string(oee.getOEE() * 100.0) + "%");

    // Publish offline
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["lineState"] = "shutdown";
    offline["finalOEE"] = oee.getOEE() * 100.0;
    offline["timestamp"] = getTimestamp();

    client.publish(LINE_STATUS_SUBJECT + ".orchestrator", offline);
    client.poll(Duration{200});

    client.disconnect();
    printStatus("Orchestrator shutdown complete.");

    return 0;
}
