/**
 * @file hmi_panel.cpp
 * @brief HMI (Human Machine Interface) panel simulator
 *
 * Simulates an operator interface that displays line status
 * and allows interactive control.
 *
 * Demo features:
 * - Real-time status display
 * - Interactive command menu
 * - Alert display
 * - OEE dashboard
 */

#include "common/demo_utils.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <map>
#include <mutex>
#include <atomic>
#include <iomanip>
#include <sstream>

using namespace gateway;
using namespace demo;

// Configuration
const std::string DEVICE_ID = "hmi-panel-001";
const std::string TOKEN = "hmi-token-001";

// Tracked state
struct LineStatus {
    std::string state = "unknown";
    int deviceCount = 0;
    int onlineCount = 0;
    double oee = 0;
    double availability = 0;
    double performance = 0;
    double quality = 0;
};

struct ConveyorStatus {
    std::string mode = "unknown";
    double speed = 0;
    double targetSpeed = 0;
};

struct ProductionStatus {
    int goodCount = 0;
    int rejectCount = 0;
    int targetCount = 10000;
    double yield = 100.0;
    double rate = 0;
};

struct QualityStatus {
    int totalScans = 0;
    int rejects = 0;
    double defectRate = 0;
};

struct TempStatus {
    double value = 0;
    std::string status = "normal";
};

// Thread-safe state container
class HMIState {
public:
    void updateLine(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("lineState")) line_.state = data["lineState"].asString();
        if (data.contains("deviceCount")) line_.deviceCount = static_cast<int>(data["deviceCount"].asInt());
        if (data.contains("onlineCount")) line_.onlineCount = static_cast<int>(data["onlineCount"].asInt());
    }

    void updateOEE(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("oee")) line_.oee = data["oee"].asDouble();
        if (data.contains("availability")) line_.availability = data["availability"].asDouble();
        if (data.contains("performance")) line_.performance = data["performance"].asDouble();
        if (data.contains("quality")) line_.quality = data["quality"].asDouble();
    }

    void updateConveyor(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("mode")) conveyor_.mode = data["mode"].asString();
        if (data.contains("currentSpeed")) conveyor_.speed = data["currentSpeed"].asDouble();
        if (data.contains("targetSpeed")) conveyor_.targetSpeed = data["targetSpeed"].asDouble();
    }

    void updateProduction(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("count")) production_.goodCount = static_cast<int>(data["count"].asInt());
        if (data.contains("rejects")) production_.rejectCount = static_cast<int>(data["rejects"].asInt());
        if (data.contains("target")) production_.targetCount = static_cast<int>(data["target"].asInt());
        if (data.contains("yield")) production_.yield = data["yield"].asDouble();
        if (data.contains("rate")) production_.rate = data["rate"].asDouble();
    }

    void updateQuality(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("totalScans")) quality_.totalScans = static_cast<int>(data["totalScans"].asInt());
        if (data.contains("rejectCount")) quality_.rejects = static_cast<int>(data["rejectCount"].asInt());
        if (data.contains("defectRate")) quality_.defectRate = data["defectRate"].asDouble();
    }

    void updateTemp(const JsonValue& data) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (data.contains("value")) temp_.value = data["value"].asDouble();
        if (data.contains("status")) temp_.status = data["status"].asString();
    }

    void addAlert(const std::string& severity, const std::string& message) {
        std::lock_guard<std::mutex> lock(mutex_);
        alerts_.push_back({severity, message, getTimestamp()});
        if (alerts_.size() > 10) {
            alerts_.erase(alerts_.begin());
        }
    }

    LineStatus getLine() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return line_;
    }

    ConveyorStatus getConveyor() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return conveyor_;
    }

    ProductionStatus getProduction() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return production_;
    }

    QualityStatus getQuality() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return quality_;
    }

    TempStatus getTemp() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return temp_;
    }

    struct Alert {
        std::string severity;
        std::string message;
        std::string time;
    };

    std::vector<Alert> getAlerts() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return alerts_;
    }

private:
    mutable std::mutex mutex_;
    LineStatus line_;
    ConveyorStatus conveyor_;
    ProductionStatus production_;
    QualityStatus quality_;
    TempStatus temp_;
    std::vector<Alert> alerts_;
};

// Display functions
void clearScreen() {
    std::cout << "\033[2J\033[H";  // ANSI clear screen
}

std::string progressBar(double percent, int width = 20) {
    int filled = static_cast<int>(percent / 100.0 * width);
    std::string bar = "[";
    for (int i = 0; i < width; i++) {
        bar += (i < filled) ? "█" : "░";
    }
    bar += "]";
    return bar;
}

void displayDashboard(const HMIState& state, const DemoConfig& config) {
    clearScreen();

    auto line = state.getLine();
    auto conv = state.getConveyor();
    auto prod = state.getProduction();
    auto qual = state.getQuality();
    auto temp = state.getTemp();
    auto alerts = state.getAlerts();

    std::cout << color::BOLD << color::CYAN;
    std::cout << "╔══════════════════════════════════════════════════════════════════════════╗\n";
    std::cout << "║                    PACKAGING LINE HMI - " << std::setw(20) << std::left << config.lineName << "            ║\n";
    std::cout << "║                    Batch: " << std::setw(20) << config.batchId << "                           ║\n";
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << color::RESET;

    // Line status
    const char* stateColor = color::GREEN;
    if (line.state == "emergency" || line.state == "fault") stateColor = color::RED;
    else if (line.state == "stopped" || line.state == "unknown") stateColor = color::YELLOW;

    std::cout << "║ " << color::BOLD << "LINE STATUS: " << stateColor << std::setw(12) << std::left << line.state << color::RESET;
    std::cout << "                    Devices: " << line.onlineCount << "/" << line.deviceCount << " online";
    std::cout << std::setw(10) << " " << "║\n";

    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";

    // Conveyor
    std::cout << "║ " << color::BOLD << "CONVEYOR" << color::RESET << std::setw(68) << " " << "║\n";
    std::cout << "║   Mode: " << std::setw(12) << conv.mode;
    std::cout << "  Speed: " << std::setw(5) << static_cast<int>(conv.speed) << " / " << std::setw(5) << static_cast<int>(conv.targetSpeed) << " units/min";
    std::cout << std::setw(24) << " " << "║\n";

    // Production
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << "║ " << color::BOLD << "PRODUCTION" << color::RESET << std::setw(66) << " " << "║\n";

    double completion = prod.targetCount > 0 ?
        static_cast<double>(prod.goodCount) / prod.targetCount * 100.0 : 0;
    std::cout << "║   Count: " << std::setw(6) << prod.goodCount << " / " << std::setw(6) << prod.targetCount;
    std::cout << "  " << progressBar(completion, 15);
    std::cout << " " << std::fixed << std::setprecision(1) << std::setw(5) << completion << "%";
    std::cout << std::setw(8) << " " << "║\n";

    std::cout << "║   Rejects: " << std::setw(5) << prod.rejectCount;
    std::cout << "  Yield: " << std::setw(5) << std::fixed << std::setprecision(1) << prod.yield << "%";
    std::cout << "  Rate: " << std::setw(5) << prod.rate << "/s";
    std::cout << std::setw(18) << " " << "║\n";

    // Quality
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << "║ " << color::BOLD << "QUALITY" << color::RESET << std::setw(69) << " " << "║\n";
    std::cout << "║   Scans: " << std::setw(6) << qual.totalScans;
    std::cout << "  Defects: " << std::setw(4) << qual.rejects;
    std::cout << "  Defect Rate: " << std::setw(5) << qual.defectRate << "%";
    std::cout << std::setw(17) << " " << "║\n";

    // Temperature
    const char* tempColor = color::GREEN;
    if (temp.status == "critical") tempColor = color::RED;
    else if (temp.status == "warning") tempColor = color::YELLOW;

    std::cout << "║ " << color::BOLD << "ENVIRONMENT" << color::RESET << std::setw(65) << " " << "║\n";
    std::cout << "║   Temperature: " << tempColor << std::setw(5) << std::fixed << std::setprecision(1) << temp.value << "°F";
    std::cout << " [" << temp.status << "]" << color::RESET;
    std::cout << std::setw(40) << " " << "║\n";

    // OEE
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << "║ " << color::BOLD << "OEE METRICS" << color::RESET << std::setw(65) << " " << "║\n";

    auto oeeColor = [](double val) {
        if (val >= 85) return color::GREEN;
        if (val >= 60) return color::YELLOW;
        return color::RED;
    };

    std::cout << "║   Availability: " << oeeColor(line.availability) << std::setw(5) << std::fixed << std::setprecision(1) << line.availability << "%" << color::RESET;
    std::cout << "  Performance: " << oeeColor(line.performance) << std::setw(5) << line.performance << "%" << color::RESET;
    std::cout << "  Quality: " << oeeColor(line.quality) << std::setw(5) << line.quality << "%" << color::RESET;
    std::cout << std::setw(3) << " " << "║\n";

    std::cout << "║   " << color::BOLD << "Overall OEE: " << oeeColor(line.oee) << std::setw(6) << line.oee << "%" << color::RESET;
    std::cout << "  " << progressBar(line.oee, 30);
    std::cout << std::setw(10) << " " << "║\n";

    // Alerts
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << "║ " << color::BOLD << "RECENT ALERTS" << color::RESET << std::setw(63) << " " << "║\n";

    if (alerts.empty()) {
        std::cout << "║   " << color::GREEN << "No active alerts" << color::RESET << std::setw(58) << " " << "║\n";
    } else {
        for (size_t i = 0; i < std::min(alerts.size(), size_t(3)); i++) {
            const auto& alert = alerts[alerts.size() - 1 - i];
            const char* alertColor = color::YELLOW;
            if (alert.severity == "critical" || alert.severity == "emergency") alertColor = color::RED;
            else if (alert.severity == "info") alertColor = color::GREEN;

            std::string msg = alert.message;
            if (msg.length() > 55) msg = msg.substr(0, 52) + "...";

            std::cout << "║   " << alertColor << "[" << std::setw(8) << alert.severity << "] " << std::setw(55) << std::left << msg << color::RESET << "║\n";
        }
    }

    // Menu
    std::cout << "╠══════════════════════════════════════════════════════════════════════════╣\n";
    std::cout << "║ " << color::BOLD << "COMMANDS:" << color::RESET << " [1]Start [2]Stop [3]Speed+ [4]Speed- [5]E-Stop [6]Reset [Q]Quit ║\n";
    std::cout << "╚══════════════════════════════════════════════════════════════════════════╝\n";
    std::cout << "\n> ";
    std::cout.flush();
}

// Input handling
std::atomic<char> lastInput{0};
std::atomic<bool> inputReady{false};

void inputThread() {
    while (g_running) {
        char c;
        if (std::cin.get(c)) {
            if (c != '\n') {
                lastInput = c;
                inputReady = true;
            }
        }
    }
}

int main() {
    installSignalHandlers();

    // Load config
    auto demoConfig = loadDemoConfig();
    auto config = createDeviceConfig(demoConfig, DEVICE_ID, TOKEN, DeviceType::Custom);
    config.customDeviceType = "hmi";

    // Create client and state
    GatewayClient client(config);
    HMIState state;

    bool connected = false;

    // Callbacks
    client.onConnected([&] {
        connected = true;
    });

    client.onDisconnected([&](ErrorCode, const std::string&) {
        connected = false;
    });

    // Connect
    if (!client.connect()) {
        std::cerr << "Failed to connect to gateway!" << std::endl;
        return 1;
    }

    // Subscribe to everything we need
    client.subscribe("factory.line1.status",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateLine(payload);
        });

    client.subscribe("factory.line1.oee",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateOEE(payload);
        });

    client.subscribe("factory.line1.conveyor.status",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateConveyor(payload);
        });

    client.subscribe("factory.line1.output",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateProduction(payload);
        });

    client.subscribe("factory.line1.quality.stats",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateQuality(payload);
        });

    client.subscribe("factory.line1.temp",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            state.updateTemp(payload);
        });

    client.subscribe("factory.line1.alerts.>",
        [&](const std::string&, const JsonValue& payload, const Message&) {
            if (payload.contains("severity") && payload.contains("message")) {
                state.addAlert(payload["severity"].asString(), payload["message"].asString());
            }
        });

    // Start input thread
    std::thread input(inputThread);

    // Main loop
    auto lastDisplay = std::chrono::steady_clock::now();
    const auto displayInterval = std::chrono::seconds(2);

    while (g_running) {
        client.poll(Duration{100});

        auto now = std::chrono::steady_clock::now();

        // Update display periodically
        if (now - lastDisplay >= displayInterval) {
            lastDisplay = now;
            displayDashboard(state, demoConfig);
        }

        // Handle input
        if (inputReady.exchange(false)) {
            char cmd = lastInput.load();

            JsonValue cmdPayload = JsonValue::object();

            switch (cmd) {
                case '1':
                    cmdPayload["action"] = "start_line";
                    client.publish("factory.line1.cmd.orchestrator", cmdPayload);
                    break;

                case '2':
                    cmdPayload["action"] = "stop_line";
                    client.publish("factory.line1.cmd.orchestrator", cmdPayload);
                    break;

                case '3':
                    cmdPayload["action"] = "setSpeed";
                    cmdPayload["value"] = state.getConveyor().targetSpeed + 20;
                    client.publish("factory.line1.conveyor.cmd", cmdPayload);
                    break;

                case '4':
                    cmdPayload["action"] = "setSpeed";
                    cmdPayload["value"] = std::max(0.0, state.getConveyor().targetSpeed - 20);
                    client.publish("factory.line1.conveyor.cmd", cmdPayload);
                    break;

                case '5':
                    cmdPayload["action"] = "emergency_stop";
                    client.publish("factory.line1.conveyor.cmd", cmdPayload);
                    break;

                case '6':
                    cmdPayload["action"] = "reset";
                    client.publish("factory.line1.conveyor.cmd", cmdPayload);
                    break;

                case 'q':
                case 'Q':
                    g_running = false;
                    break;
            }

            // Refresh display after command
            displayDashboard(state, demoConfig);
        }
    }

    if (input.joinable()) {
        input.detach();
    }

    clearScreen();
    std::cout << "HMI Panel shutdown.\n";

    client.disconnect();
    return 0;
}
