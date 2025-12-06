/**
 * @file demo_utils.h
 * @brief Shared utilities for demo devices
 */

#pragma once

#include <gateway/gateway_device.h>
#include <string>
#include <fstream>
#include <chrono>
#include <random>
#include <atomic>
#include <csignal>

namespace demo {

// Global running flag for signal handling
extern std::atomic<bool> g_running;

/**
 * @brief Install signal handlers for graceful shutdown
 */
void installSignalHandlers();

/**
 * @brief Demo configuration loaded from JSON
 */
struct DemoConfig {
    std::string gatewayUrl;
    bool insecure = true;

    std::string lineId;
    std::string lineName;

    std::string batchId;
    std::string product;
    std::string lotNumber;
    int targetCount = 10000;
};

/**
 * @brief Load demo configuration from file
 */
DemoConfig loadDemoConfig(const std::string& path = "../config/demo_config.json");

/**
 * @brief Create a gateway config for a device
 */
gateway::GatewayConfig createDeviceConfig(
    const DemoConfig& demo,
    const std::string& deviceId,
    const std::string& token,
    gateway::DeviceType type
);

/**
 * @brief Format current time as ISO 8601
 */
std::string getTimestamp();

/**
 * @brief Get current time in milliseconds since epoch
 */
int64_t getTimeMs();

/**
 * @brief Random number generator helper
 */
class Random {
public:
    Random();

    double uniform(double min, double max);
    int uniformInt(int min, int max);
    double gaussian(double mean, double stddev);
    bool chance(double probability);

    template<typename T>
    const T& pick(const std::vector<T>& items) {
        return items[uniformInt(0, static_cast<int>(items.size()) - 1)];
    }

private:
    std::mt19937 gen_;
};

/**
 * @brief Simulated sensor value with noise and drift
 */
class SimulatedValue {
public:
    SimulatedValue(double baseValue, double noiseStddev, double driftRate = 0.0);

    double read();
    void setBase(double value);
    double getBase() const { return baseValue_; }

    // Simulate anomaly
    void injectAnomaly(double magnitude, int durationMs);

private:
    double baseValue_;
    double noiseStddev_;
    double driftRate_;
    double currentDrift_ = 0.0;

    double anomalyMagnitude_ = 0.0;
    std::chrono::steady_clock::time_point anomalyEnd_;

    Random rng_;
};

/**
 * @brief Print helpers for demo visibility
 */
void printBanner(const std::string& deviceName);
void printStatus(const std::string& message);
void printPublish(const std::string& subject, const std::string& summary);
void printReceive(const std::string& subject, const std::string& summary);
void printWarning(const std::string& message);
void printError(const std::string& message);
void printAlert(const std::string& severity, const std::string& message);

/**
 * @brief Console colors (ANSI)
 */
namespace color {
    constexpr const char* RESET = "\033[0m";
    constexpr const char* RED = "\033[31m";
    constexpr const char* GREEN = "\033[32m";
    constexpr const char* YELLOW = "\033[33m";
    constexpr const char* BLUE = "\033[34m";
    constexpr const char* MAGENTA = "\033[35m";
    constexpr const char* CYAN = "\033[36m";
    constexpr const char* WHITE = "\033[37m";
    constexpr const char* BOLD = "\033[1m";
}

} // namespace demo
