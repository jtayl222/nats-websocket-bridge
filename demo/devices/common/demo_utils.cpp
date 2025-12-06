/**
 * @file demo_utils.cpp
 * @brief Implementation of demo utilities
 */

#include "demo_utils.h"
#include <iostream>
#include <iomanip>
#include <ctime>
#include <fstream>
#include <sstream>

namespace demo {

std::atomic<bool> g_running{true};

static void signalHandler(int sig) {
    std::cout << "\n" << color::YELLOW << "[SIGNAL] Shutdown requested (signal "
              << sig << ")" << color::RESET << std::endl;
    g_running = false;
}

void installSignalHandlers() {
    std::signal(SIGINT, signalHandler);
    std::signal(SIGTERM, signalHandler);
}

DemoConfig loadDemoConfig(const std::string& path) {
    DemoConfig config;

    // For simplicity, use hardcoded defaults
    // In production, parse JSON file
    config.gatewayUrl = "wss://localhost:5000/ws";
    config.insecure = true;
    config.lineId = "line1";
    config.lineName = "Packaging Line 1";
    config.batchId = "BATCH-2024-001";
    config.product = "Aspirin 500mg";
    config.lotNumber = "LOT-A7823";
    config.targetCount = 10000;

    // Try to read from environment
    if (const char* url = std::getenv("GATEWAY_URL")) {
        config.gatewayUrl = url;
    }

    return config;
}

gateway::GatewayConfig createDeviceConfig(
    const DemoConfig& demo,
    const std::string& deviceId,
    const std::string& token,
    gateway::DeviceType type
) {
    gateway::GatewayConfig config;

    config.gatewayUrl = demo.gatewayUrl;
    config.deviceId = deviceId;
    config.authToken = token;
    config.deviceType = type;

    // TLS settings for demo
    config.tls.verifyPeer = !demo.insecure;

    // Reconnection
    config.reconnect.enabled = true;
    config.reconnect.maxAttempts = 0;  // Unlimited

    // Heartbeat
    config.heartbeat.enabled = true;
    config.heartbeat.interval = gateway::Duration{30000};

    // Logging
    config.logging.level = 2;  // Info

    return config;
}

std::string getTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto time = std::chrono::system_clock::to_time_t(now);
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;

    std::ostringstream oss;
    oss << std::put_time(std::gmtime(&time), "%Y-%m-%dT%H:%M:%S");
    oss << '.' << std::setfill('0') << std::setw(3) << ms.count() << 'Z';
    return oss.str();
}

int64_t getTimeMs() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
}

// Random implementation
Random::Random() : gen_(std::random_device{}()) {}

double Random::uniform(double min, double max) {
    std::uniform_real_distribution<double> dist(min, max);
    return dist(gen_);
}

int Random::uniformInt(int min, int max) {
    std::uniform_int_distribution<int> dist(min, max);
    return dist(gen_);
}

double Random::gaussian(double mean, double stddev) {
    std::normal_distribution<double> dist(mean, stddev);
    return dist(gen_);
}

bool Random::chance(double probability) {
    return uniform(0.0, 1.0) < probability;
}

// SimulatedValue implementation
SimulatedValue::SimulatedValue(double baseValue, double noiseStddev, double driftRate)
    : baseValue_(baseValue), noiseStddev_(noiseStddev), driftRate_(driftRate)
{}

double SimulatedValue::read() {
    // Apply noise
    double value = baseValue_ + rng_.gaussian(0.0, noiseStddev_);

    // Apply drift
    currentDrift_ += rng_.gaussian(0.0, driftRate_);
    currentDrift_ = std::max(-5.0, std::min(5.0, currentDrift_));  // Clamp drift
    value += currentDrift_;

    // Apply anomaly if active
    if (std::chrono::steady_clock::now() < anomalyEnd_) {
        value += anomalyMagnitude_;
    }

    return value;
}

void SimulatedValue::setBase(double value) {
    baseValue_ = value;
}

void SimulatedValue::injectAnomaly(double magnitude, int durationMs) {
    anomalyMagnitude_ = magnitude;
    anomalyEnd_ = std::chrono::steady_clock::now() +
                  std::chrono::milliseconds(durationMs);
}

// Print helpers
static std::string currentTimeStr() {
    auto now = std::chrono::system_clock::now();
    auto time = std::chrono::system_clock::to_time_t(now);
    std::ostringstream oss;
    oss << std::put_time(std::localtime(&time), "%H:%M:%S");
    return oss.str();
}

void printBanner(const std::string& deviceName) {
    std::cout << color::BOLD << color::CYAN;
    std::cout << "\n";
    std::cout << "╔════════════════════════════════════════════════════════╗\n";
    std::cout << "║  PACKAGING LINE DEMO - " << std::setw(32) << std::left << deviceName << "║\n";
    std::cout << "╚════════════════════════════════════════════════════════╝\n";
    std::cout << color::RESET << std::endl;
}

void printStatus(const std::string& message) {
    std::cout << color::WHITE << "[" << currentTimeStr() << "] "
              << color::RESET << message << std::endl;
}

void printPublish(const std::string& subject, const std::string& summary) {
    std::cout << color::GREEN << "[" << currentTimeStr() << "] ▶ PUBLISH "
              << color::WHITE << subject << color::RESET
              << " → " << summary << std::endl;
}

void printReceive(const std::string& subject, const std::string& summary) {
    std::cout << color::BLUE << "[" << currentTimeStr() << "] ◀ RECEIVE "
              << color::WHITE << subject << color::RESET
              << " → " << summary << std::endl;
}

void printWarning(const std::string& message) {
    std::cout << color::YELLOW << "[" << currentTimeStr() << "] ⚠ WARNING: "
              << message << color::RESET << std::endl;
}

void printError(const std::string& message) {
    std::cout << color::RED << "[" << currentTimeStr() << "] ✖ ERROR: "
              << message << color::RESET << std::endl;
}

void printAlert(const std::string& severity, const std::string& message) {
    const char* col = color::YELLOW;
    if (severity == "critical" || severity == "emergency") {
        col = color::RED;
    }
    std::cout << col << color::BOLD << "[" << currentTimeStr() << "] 🚨 "
              << severity << ": " << message << color::RESET << std::endl;
}

} // namespace demo
