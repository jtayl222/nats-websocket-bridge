/**
 * @file logger.h
 * @brief Logging interface for the Gateway Device SDK
 */

#pragma once

#include "config.h"
#include <string>
#include <memory>
#include <functional>
#include <sstream>
#include <chrono>
#include <iomanip>
#include <ctime>

namespace gateway {

/**
 * @brief Log levels
 */
enum class LogLevel {
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
    Off = 6
};

/**
 * @brief Convert LogLevel to string
 */
inline const char* logLevelToString(LogLevel level) {
    switch (level) {
        case LogLevel::Trace: return "TRACE";
        case LogLevel::Debug: return "DEBUG";
        case LogLevel::Info: return "INFO";
        case LogLevel::Warn: return "WARN";
        case LogLevel::Error: return "ERROR";
        case LogLevel::Fatal: return "FATAL";
        case LogLevel::Off: return "OFF";
        default: return "UNKNOWN";
    }
}

/**
 * @brief Log entry structure
 */
struct LogEntry {
    LogLevel level;
    std::string message;
    std::string category;
    std::chrono::system_clock::time_point timestamp;
    std::thread::id threadId;
};

/**
 * @brief Custom log handler type
 */
using LogHandler = std::function<void(const LogEntry& entry)>;

/**
 * @brief Logger interface
 */
class Logger {
public:
    virtual ~Logger() = default;

    virtual void log(LogLevel level, const std::string& category, const std::string& message) = 0;

    void trace(const std::string& category, const std::string& message) {
        log(LogLevel::Trace, category, message);
    }

    void debug(const std::string& category, const std::string& message) {
        log(LogLevel::Debug, category, message);
    }

    void info(const std::string& category, const std::string& message) {
        log(LogLevel::Info, category, message);
    }

    void warn(const std::string& category, const std::string& message) {
        log(LogLevel::Warn, category, message);
    }

    void error(const std::string& category, const std::string& message) {
        log(LogLevel::Error, category, message);
    }

    void fatal(const std::string& category, const std::string& message) {
        log(LogLevel::Fatal, category, message);
    }

    virtual void setLevel(LogLevel level) = 0;
    virtual LogLevel getLevel() const = 0;
    virtual void setEnabled(bool enabled) = 0;
    virtual bool isEnabled() const = 0;
};

/**
 * @brief Default console logger implementation
 */
class ConsoleLogger : public Logger {
public:
    ConsoleLogger() : level_(LogLevel::Info), enabled_(true), showTimestamp_(true), showThreadId_(false) {}

    explicit ConsoleLogger(const LogConfig& config)
        : level_(static_cast<LogLevel>(config.level))
        , enabled_(config.enabled)
        , showTimestamp_(config.timestamps)
        , showThreadId_(config.threadId)
    {}

    void log(LogLevel level, const std::string& category, const std::string& message) override {
        if (!enabled_ || level < level_) return;

        std::ostringstream oss;

        if (showTimestamp_) {
            auto now = std::chrono::system_clock::now();
            auto time = std::chrono::system_clock::to_time_t(now);
            auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                now.time_since_epoch()) % 1000;
            oss << std::put_time(std::localtime(&time), "%Y-%m-%d %H:%M:%S");
            oss << '.' << std::setfill('0') << std::setw(3) << ms.count() << ' ';
        }

        oss << '[' << logLevelToString(level) << ']';

        if (showThreadId_) {
            oss << " [" << std::this_thread::get_id() << ']';
        }

        if (!category.empty()) {
            oss << " [" << category << ']';
        }

        oss << ' ' << message;

        // Thread-safe output
        std::lock_guard<std::mutex> lock(mutex_);
        if (level >= LogLevel::Error) {
            std::cerr << oss.str() << std::endl;
        } else {
            std::cout << oss.str() << std::endl;
        }
    }

    void setLevel(LogLevel level) override { level_ = level; }
    LogLevel getLevel() const override { return level_; }
    void setEnabled(bool enabled) override { enabled_ = enabled; }
    bool isEnabled() const override { return enabled_; }

    void setShowTimestamp(bool show) { showTimestamp_ = show; }
    void setShowThreadId(bool show) { showThreadId_ = show; }

private:
    LogLevel level_;
    bool enabled_;
    bool showTimestamp_;
    bool showThreadId_;
    mutable std::mutex mutex_;
};

/**
 * @brief Custom logger that forwards to a user-provided handler
 */
class CustomLogger : public Logger {
public:
    explicit CustomLogger(LogHandler handler)
        : handler_(std::move(handler)), level_(LogLevel::Info), enabled_(true) {}

    void log(LogLevel level, const std::string& category, const std::string& message) override {
        if (!enabled_ || level < level_ || !handler_) return;

        LogEntry entry;
        entry.level = level;
        entry.category = category;
        entry.message = message;
        entry.timestamp = std::chrono::system_clock::now();
        entry.threadId = std::this_thread::get_id();

        handler_(entry);
    }

    void setLevel(LogLevel level) override { level_ = level; }
    LogLevel getLevel() const override { return level_; }
    void setEnabled(bool enabled) override { enabled_ = enabled; }
    bool isEnabled() const override { return enabled_; }

    void setHandler(LogHandler handler) { handler_ = std::move(handler); }

private:
    LogHandler handler_;
    LogLevel level_;
    bool enabled_;
};

/**
 * @brief Null logger that discards all messages
 */
class NullLogger : public Logger {
public:
    void log(LogLevel, const std::string&, const std::string&) override {}
    void setLevel(LogLevel) override {}
    LogLevel getLevel() const override { return LogLevel::Off; }
    void setEnabled(bool) override {}
    bool isEnabled() const override { return false; }
};

/**
 * @brief Stream-style logging helper
 */
class LogStream {
public:
    LogStream(Logger& logger, LogLevel level, const std::string& category)
        : logger_(logger), level_(level), category_(category) {}

    ~LogStream() {
        logger_.log(level_, category_, stream_.str());
    }

    template<typename T>
    LogStream& operator<<(const T& value) {
        stream_ << value;
        return *this;
    }

private:
    Logger& logger_;
    LogLevel level_;
    std::string category_;
    std::ostringstream stream_;
};

// Macros for convenient logging (optional, users can use Logger directly)
#define GATEWAY_LOG(logger, level, category) \
    if ((logger).isEnabled() && (level) >= (logger).getLevel()) \
        gateway::LogStream(logger, level, category)

#define GATEWAY_TRACE(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Trace, category)
#define GATEWAY_DEBUG(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Debug, category)
#define GATEWAY_INFO(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Info, category)
#define GATEWAY_WARN(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Warn, category)
#define GATEWAY_ERROR(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Error, category)
#define GATEWAY_FATAL(logger, category) GATEWAY_LOG(logger, gateway::LogLevel::Fatal, category)

} // namespace gateway
