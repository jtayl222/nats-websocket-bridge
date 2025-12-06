/**
 * @file production_counter.cpp
 * @brief Production counter simulator for packaging line demo
 *
 * Simulates a photoelectric counter tracking packages produced.
 * Integrates with conveyor speed to determine count rate.
 *
 * Demo features:
 * - Count rate based on conveyor speed
 * - Good vs reject counting (from vision scanner)
 * - Batch completion tracking
 * - OEE metrics contribution
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <atomic>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "sensor-counter-001";
const std::string TOKEN = "counter-token-001";
const std::string OUTPUT_SUBJECT = "factory.line1.output";
const std::string STATS_SUBJECT = "factory.line1.production.stats";
const int PUBLISH_INTERVAL_MS = 5000;

class ProductionCounter {
public:
    ProductionCounter(int targetCount = 10000)
        : targetCount_(targetCount), totalCount_(0), goodCount_(0), rejectCount_(0),
          conveyorSpeed_(0), lastCountTime_(std::chrono::steady_clock::now())
    {}

    void setConveyorSpeed(double speed) {
        conveyorSpeed_ = speed;
    }

    void addReject() {
        rejectCount_++;
        totalCount_++;
    }

    // Simulate counting based on conveyor speed
    // Returns number of new items counted
    int update() {
        if (conveyorSpeed_ <= 0) {
            return 0;
        }

        auto now = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(now - lastCountTime_).count();
        lastCountTime_ = now;

        // Items per second = speed / 60 (speed is units/min, ~1 item per unit)
        double itemsPerSecond = conveyorSpeed_ / 60.0;
        int newItems = static_cast<int>(itemsPerSecond * elapsed);

        // Accumulate fractional items
        fractionalItems_ += (itemsPerSecond * elapsed) - newItems;
        if (fractionalItems_ >= 1.0) {
            newItems += static_cast<int>(fractionalItems_);
            fractionalItems_ -= static_cast<int>(fractionalItems_);
        }

        goodCount_ += newItems;
        totalCount_ += newItems;

        return newItems;
    }

    int getTotalCount() const { return totalCount_; }
    int getGoodCount() const { return goodCount_; }
    int getRejectCount() const { return rejectCount_; }
    int getTargetCount() const { return targetCount_; }

    double getCompletionPercent() const {
        return targetCount_ > 0 ?
            static_cast<double>(goodCount_) / targetCount_ * 100.0 : 0.0;
    }

    double getYield() const {
        return totalCount_ > 0 ?
            static_cast<double>(goodCount_) / totalCount_ * 100.0 : 100.0;
    }

    bool isTargetReached() const {
        return goodCount_ >= targetCount_;
    }

    void reset(int newTarget = -1) {
        if (newTarget > 0) {
            targetCount_ = newTarget;
        }
        totalCount_ = 0;
        goodCount_ = 0;
        rejectCount_ = 0;
        fractionalItems_ = 0;
    }

private:
    int targetCount_;
    std::atomic<int> totalCount_;
    std::atomic<int> goodCount_;
    std::atomic<int> rejectCount_;
    double conveyorSpeed_;
    double fractionalItems_ = 0;
    std::chrono::steady_clock::time_point lastCountTime_;
};

int main() {
    installSignalHandlers();
    printBanner("PRODUCTION COUNTER");

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Sensor);

    printStatus("Device ID: " + DEVICE_ID);
    printStatus("Gateway: " + demoConfig.gatewayUrl);
    printStatus("Output subject: " + OUTPUT_SUBJECT);
    printStatus("Target count: " + std::to_string(demoConfig.targetCount));

    // Create client and counter
    GatewayClient client(config);
    ProductionCounter counter(demoConfig.targetCount);

    bool targetReachedNotified = false;
    auto startTime = std::chrono::steady_clock::now();

    // Callbacks
    client.onConnected([&] {
        printStatus("✓ Connected and authenticated!");

        // Publish initial status
        JsonValue status = JsonValue::object();
        status["online"] = true;
        status["deviceId"] = DEVICE_ID;
        status["targetCount"] = counter.getTargetCount();
        status["currentCount"] = counter.getTotalCount();
        status["batch"] = demoConfig.batchId;
        status["lot"] = demoConfig.lotNumber;

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

    // Subscribe to conveyor status to get speed
    client.subscribe("factory.line1.conveyor.status",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            if (payload.contains("currentSpeed")) {
                double speed = payload["currentSpeed"].asDouble();
                counter.setConveyorSpeed(speed);
            }
        });

    // Subscribe to rejects from vision scanner
    client.subscribe("factory.line1.quality.rejects",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            counter.addReject();
        });

    // Subscribe to commands
    client.subscribe("factory.line1.cmd." + DEVICE_ID + ".>",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printReceive(subject, "Command received");

            if (payload.contains("action")) {
                std::string action = payload["action"].asString();

                if (action == "reset") {
                    int newTarget = payload.contains("target") ?
                                   static_cast<int>(payload["target"].asInt()) : -1;
                    counter.reset(newTarget);
                    targetReachedNotified = false;
                    startTime = std::chrono::steady_clock::now();
                    printStatus("Counter reset" + (newTarget > 0 ?
                               " (new target: " + std::to_string(newTarget) + ")" : ""));

                } else if (action == "set_target") {
                    if (payload.contains("value")) {
                        // Just update target without resetting counts
                        printStatus("Target update not implemented - use reset");
                    }

                } else if (action == "status") {
                    JsonValue status = JsonValue::object();
                    status["totalCount"] = counter.getTotalCount();
                    status["goodCount"] = counter.getGoodCount();
                    status["rejectCount"] = counter.getRejectCount();
                    status["targetCount"] = counter.getTargetCount();
                    status["completion"] = counter.getCompletionPercent();
                    status["yield"] = counter.getYield();

                    client.publish(STATS_SUBJECT, status);
                }
            }
        });

    // Subscribe to emergency
    client.subscribe("factory.line1.emergency",
        [&](const std::string& subject, const JsonValue& payload, const Message&) {
            printAlert("EMERGENCY", "Emergency - counter paused");
            counter.setConveyorSpeed(0);
        });

    printStatus("Production counter ready.\n");

    // Timing
    auto lastPublish = std::chrono::steady_clock::now();
    int lastCount = 0;

    while (g_running) {
        client.poll(Duration{100});

        if (!client.isConnected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }

        // Update counter
        int newItems = counter.update();

        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - lastPublish);

        if (elapsed.count() >= PUBLISH_INTERVAL_MS) {
            lastPublish = now;

            int currentCount = counter.getTotalCount();
            int countDelta = currentCount - lastCount;
            lastCount = currentCount;

            // Calculate rate
            double rate = countDelta / (PUBLISH_INTERVAL_MS / 1000.0);

            // Calculate elapsed time
            auto runTime = std::chrono::duration_cast<std::chrono::seconds>(now - startTime);

            // Build output message
            JsonValue output = JsonValue::object();
            output["count"] = counter.getGoodCount();
            output["total"] = counter.getTotalCount();
            output["rejects"] = counter.getRejectCount();
            output["target"] = counter.getTargetCount();
            output["completion"] = counter.getCompletionPercent();
            output["yield"] = counter.getYield();
            output["rate"] = rate;  // items per second
            output["runtimeSeconds"] = static_cast<int64_t>(runTime.count());
            output["timestamp"] = getTimestamp();
            output["batch"] = demoConfig.batchId;
            output["lot"] = demoConfig.lotNumber;

            client.publish(OUTPUT_SUBJECT, output);

            std::ostringstream summary;
            summary << std::fixed << std::setprecision(1);
            summary << counter.getGoodCount() << "/" << counter.getTargetCount();
            summary << " (" << counter.getCompletionPercent() << "%)";
            summary << " Rate: " << rate << "/s";

            printPublish(OUTPUT_SUBJECT, summary.str());

            // Check for batch completion
            if (counter.isTargetReached() && !targetReachedNotified) {
                targetReachedNotified = true;

                std::cout << "\n";
                printAlert("INFO", "🎉 BATCH TARGET REACHED!");
                std::cout << "\n";

                JsonValue complete = JsonValue::object();
                complete["type"] = "batch_complete";
                complete["batch"] = demoConfig.batchId;
                complete["lot"] = demoConfig.lotNumber;
                complete["goodCount"] = counter.getGoodCount();
                complete["rejectCount"] = counter.getRejectCount();
                complete["yield"] = counter.getYield();
                complete["runtimeSeconds"] = static_cast<int64_t>(runTime.count());
                complete["timestamp"] = getTimestamp();

                client.publish("factory.line1.batch.complete", complete);

                // Also publish alert
                JsonValue alert = JsonValue::object();
                alert["severity"] = "info";
                alert["type"] = "batch_complete";
                alert["message"] = "Batch " + demoConfig.batchId + " completed!";
                alert["count"] = counter.getGoodCount();
                alert["timestamp"] = getTimestamp();

                client.publish("factory.line1.alerts.info", alert);
            }
        }
    }

    // Final stats
    auto runTime = std::chrono::duration_cast<std::chrono::seconds>(
        std::chrono::steady_clock::now() - startTime);

    printStatus("\n=== Final Production Statistics ===");
    printStatus("Total produced: " + std::to_string(counter.getTotalCount()));
    printStatus("Good count: " + std::to_string(counter.getGoodCount()));
    printStatus("Reject count: " + std::to_string(counter.getRejectCount()));
    printStatus("Yield: " + std::to_string(counter.getYield()) + "%");
    printStatus("Completion: " + std::to_string(counter.getCompletionPercent()) + "%");
    printStatus("Runtime: " + std::to_string(runTime.count()) + " seconds");

    // Publish offline
    JsonValue offline = JsonValue::object();
    offline["online"] = false;
    offline["finalCount"] = counter.getGoodCount();
    offline["finalYield"] = counter.getYield();
    offline["timestamp"] = getTimestamp();

    client.publish("factory.line1.status." + DEVICE_ID, offline);
    client.poll(Duration{200});

    client.disconnect();
    printStatus("Production counter shutdown complete.");

    return 0;
}
