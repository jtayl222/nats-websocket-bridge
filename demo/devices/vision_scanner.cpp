/**
 * @file vision_scanner.cpp
 * @brief Vision quality scanner simulator for packaging line demo
 *
 * Simulates an optical inspection system that detects packaging defects.
 * Publishes reject events and quality statistics.
 *
 * Demo features:
 * - Realistic defect detection simulation
 * - Defect rate spike injection for alerts
 * - Quality statistics aggregation
 * - Integration with production counter
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <map>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "sensor-vision-001";
const std::string TOKEN = "vision-token-001";
const std::string REJECTS_SUBJECT = "factory.line1.quality.rejects";
const std::string STATS_SUBJECT = "factory.line1.quality.stats";
const std::string ALERTS_SUBJECT = "factory.line1.alerts";
const int SCAN_INTERVAL_MS = 500;  // Scan every 500ms (simulating line speed)
const int STATS_INTERVAL_MS = 10000;

// Defect types
const std::vector<std::string> DEFECT_TYPES = {
    "label_misalignment",
    "missing_label",
    "damaged_package",
    "wrong_orientation",
    "contamination",
    "barcode_unreadable",
    "seal_incomplete",
    "print_defect"
};

// Defect probabilities (per type, must sum to 1.0)
const std::map<std::string, double> DEFECT_WEIGHTS = {
    {"label_misalignment", 0.30},
    {"missing_label", 0.10},
    {"damaged_package", 0.15},
    {"wrong_orientation", 0.10},
    {"contamination", 0.05},
    {"barcode_unreadable", 0.15},
    {"seal_incomplete", 0.10},
    {"print_defect", 0.05}
};

class VisionScanner {
public:
    VisionScanner() : defectRate_(0.02), highDefectMode_(false) {}

    struct ScanResult {
        bool passed;
        std::string defectType;
        double confidence;
    };

    ScanResult scan() {
        ScanResult result;
        totalScans_++;

        double effectiveRate = highDefectMode_ ? highDefectRate_ : defectRate_;

        if (rng_.chance(effectiveRate)) {
            // Defect detected
            result.passed = false;
            result.defectType = selectDefect();
            result.confidence = 0.85 + rng_.uniform(0.0, 0.15);
            rejectCount_++;
            defectCounts_[result.defectType]++;
        } else {
            result.passed = true;
            result.defectType = "";
            result.confidence = 0.95 + rng_.uniform(0.0, 0.05);
            passCount_++;
        }

        return result;
    }

    void setDefectRate(double rate) {
        defectRate_ = std::max(0.0, std::min(1.0, rate));
    }

    void setHighDefectMode(bool enabled, double rate = 0.15) {
        highDefectMode_ = enabled;
        highDefectRate_ = rate;
    }

    bool isHighDefectMode() const { return highDefectMode_; }

    int getTotalScans() const { return totalScans_; }
    int getPassCount() const { return passCount_; }
    int getRejectCount() const { return rejectCount_; }

    double getYield() const {
        return totalScans_ > 0 ? static_cast<double>(passCount_) / totalScans_ : 1.0;
    }

    double getCurrentDefectRate() const {
        return totalScans_ > 0 ? static_cast<double>(rejectCount_) / totalScans_ : 0.0;
    }

    const std::map<std::string, int>& getDefectCounts() const {
        return defectCounts_;
    }

    void resetStats() {
        totalScans_ = 0;
        passCount_ = 0;
        rejectCount_ = 0;
        defectCounts_.clear();
    }

private:
    std::string selectDefect() {
        double r = rng_.uniform(0.0, 1.0);
        double cumulative = 0.0;

        for (const auto& [type, weight] : DEFECT_WEIGHTS) {
            cumulative += weight;
            if (r <= cumulative) {
                return type;
            }
        }

        return DEFECT_TYPES[0];
    }

    Random rng_;
    double defectRate_;
    double highDefectRate_ = 0.15;
    bool highDefectMode_;

    int totalScans_ = 0;
    int passCount_ = 0;
    int rejectCount_ = 0;
    std::map<std::string, int> defectCounts_;
};

int main() {
    installSignalHandlers();
    printBanner("VISION QUALITY SCANNER");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Sensor);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("Rejects subject: " + REJECTS_SUBJECT);
    printStatus("Stats subject: " + STATS_SUBJECT);

    // Create client and scanner
    GatewayClient client(config);
    VisionScanner scanner;

    // Track alerts
    bool defectRateAlertActive = false;
    int consecutiveRejects = 0;
    const int CONSECUTIVE_REJECT_THRESHOLD = 5;
    const double DEFECT_RATE_ALERT_THRESHOLD = 0.05;

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");

        // Publish online status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["type"] = "vision_scanner";
        status["resolution"] = "4K";
        status["batch"] = demoConfig.batchId;

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

    // Subscribe to commands
    client.subscribe("factory.line1.cmd." + DEVICE_ID + ".>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printReceive(subject, "Command received");

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "set_defect_rate") {
                    if (payload.contains("value")) {
                        double rate = payload["value"].asDouble();
                        scanner.setDefectRate(rate);
                        printStatus("Defect rate set to " + std::to_string(rate * 100) + "%");
                    }

                } else if (action == "inject_high_defects") {
                    bool enabled = !scanner.isHighDefectMode();
                    double rate = payload.contains("rate") ? payload["rate"].asDouble() : 0.15;
                    scanner.setHighDefectMode(enabled, rate);

                    if (enabled) {
                        printWarning("HIGH DEFECT MODE ENABLED (" +
                                   std::to_string(rate * 100) + "% rate)");
                    } else {
                        printStatus("High defect mode disabled");
                    }

                } else if (action == "reset_stats") {
                    scanner.resetStats();
                    defectRateAlertActive = false;
                    consecutiveRejects = 0;
                    printStatus("Statistics reset");

                } else if (action == "status") {
                    // Publish current status
                    JsonValue status = JsonValue::object();
                    status["totalScans"] = scanner.getTotalScans();
                    status["passCount"] = scanner.getPassCount();
                    status["rejectCount"] = scanner.getRejectCount();
                    status["yield"] = scanner.getYield() * 100.0;
                    status["defectRate"] = scanner.getCurrentDefectRate() * 100.0;
                    status["highDefectMode"] = scanner.isHighDefectMode();

                    client.publish(STATS_SUBJECT, status);
                }
            }
        });

    // Subscribe to emergency
    client.subscribe("factory.line1.emergency",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printAlert("EMERGENCY", "Emergency - scanning suspended");
        });

    // Subscribe to conveyor status to know when line is running
    bool lineRunning = false;
    client.subscribe("factory.line1.conveyor.status",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("mode")) {
                std::string mode = payload["mode"].asString();
                lineRunning = (mode == "running");
            }
        });

    printStatus("Vision scanner ready. Waiting for line to start...\n");

    // Timing
    auto lastScan = std::chrono::steady_clock::now();
    auto lastStats = lastScan;

    while (g_running) {
        client.poll(Duration{50});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        auto now = std::chrono::steady_clock::now();

        // Perform scan if line is running
        if (lineRunning) {
            auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastScan);

            if (elapsed.count() >= SCAN_INTERVAL_MS) {
                lastScan = now;

                auto result = scanner.scan();

                if (!result.passed) {
                    consecutiveRejects++;

                    // Publish reject event
                    JsonValue reject = JsonValue::object();
                    reject["defect"] = result.defectType;
                    reject["confidence"] = result.confidence;
                    reject["scanNumber"] = scanner.getTotalScans();
                    reject["timestamp"] = getTimestamp();
                    reject["batch"] = demoConfig.batchId;
                    reject["lot"] = demoConfig.lotNumber;

                    client.publish(REJECTS_SUBJECT, reject);

                    printPublish(REJECTS_SUBJECT, result.defectType +
                               " (conf: " + std::to_string(static_cast<int>(result.confidence * 100)) + "%)");

                    // Check for consecutive reject alert
                    if (consecutiveRejects >= CONSECUTIVE_REJECT_THRESHOLD) {
                        JsonValue alert = JsonValue::object();
                        alert["severity"] = "warning";
                        alert["type"] = "consecutive_rejects";
                        alert["count"] = consecutiveRejects;
                        alert["device"] = DEVICE_ID;
                        alert["message"] = std::to_string(consecutiveRejects) + " consecutive rejects detected";
                        alert["timestamp"] = getTimestamp();

                        client.publish(ALERTS_SUBJECT + ".warning", alert);
                        printAlert("WARNING", std::to_string(consecutiveRejects) + " consecutive rejects!");
                    }

                } else {
                    consecutiveRejects = 0;  // Reset on pass
                }

                // Check defect rate for alert
                double defectRate = scanner.getCurrentDefectRate();
                if (scanner.getTotalScans() >= 100) {  // Only alert after enough samples
                    if (defectRate > DEFECT_RATE_ALERT_THRESHOLD && !defectRateAlertActive) {
                        defectRateAlertActive = true;

                        JsonValue alert = JsonValue::object();
                        alert["severity"] = "critical";
                        alert["type"] = "high_defect_rate";
                        alert["defectRate"] = defectRate * 100.0;
                        alert["threshold"] = DEFECT_RATE_ALERT_THRESHOLD * 100.0;
                        alert["device"] = DEVICE_ID;
                        alert["message"] = "Defect rate exceeded threshold";
                        alert["timestamp"] = getTimestamp();

                        client.publish(ALERTS_SUBJECT + ".critical", alert);
                        printAlert("CRITICAL", "Defect rate " +
                                  std::to_string(defectRate * 100.0) + "% exceeds threshold!");

                    } else if (defectRate <= DEFECT_RATE_ALERT_THRESHOLD && defectRateAlertActive) {
                        defectRateAlertActive = false;

                        JsonValue alert = JsonValue::object();
                        alert["severity"] = "info";
                        alert["type"] = "defect_rate_normal";
                        alert["defectRate"] = defectRate * 100.0;
                        alert["device"] = DEVICE_ID;
                        alert["message"] = "Defect rate returned to normal";
                        alert["timestamp"] = getTimestamp();

                        client.publish(ALERTS_SUBJECT + ".info", alert);
                        printStatus("Defect rate returned to normal");
                    }
                }
            }
        }

        // Publish statistics periodically
        auto statsElapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastStats);
        if (statsElapsed.count() >= STATS_INTERVAL_MS && scanner.getTotalScans() > 0) {
            lastStats = now;

            JsonValue stats = JsonValue::object();
            stats["totalScans"] = scanner.getTotalScans();
            stats["passCount"] = scanner.getPassCount();
            stats["rejectCount"] = scanner.getRejectCount();
            stats["yield"] = scanner.getYield() * 100.0;
            stats["defectRate"] = scanner.getCurrentDefectRate() * 100.0;
            stats["timestamp"] = getTimestamp();
            stats["batch"] = demoConfig.batchId;

            // Defect breakdown
            JsonValue defects = JsonValue::object();
            for (const auto& [type, count] : scanner.getDefectCounts()) {
                defects[type] = count;
            }
            stats["defectsByType"] = defects;

            client.publish(STATS_SUBJECT, stats);

            printPublish(STATS_SUBJECT,
                "Scans: " + std::to_string(scanner.getTotalScans()) +
                ", Yield: " + std::to_string(static_cast<int>(scanner.getYield() * 100)) + "%");
        }
    }

    // Final stats
    printStatus("\n=== Final Quality Statistics ===");
    printStatus("Total scans: " + std::to_string(scanner.getTotalScans()));
    printStatus("Passed: " + std::to_string(scanner.getPassCount()));
    printStatus("Rejected: " + std::to_string(scanner.getRejectCount()));
    printStatus("Yield: " + std::to_string(scanner.getYield() * 100.0) + "%");

    // Publish offline status
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["deviceId"] = DEVICE_ID;
    offline["finalYield"] = scanner.getYield() * 100.0;
    offline["timestamp"] = getTimestamp();

    client.publish("factory.line1.status." + DEVICE_ID, offline);
    client.poll(Duration{200});

    client.disconnect();
    printStatus("Vision scanner shutdown complete.");

    return 0;
}
