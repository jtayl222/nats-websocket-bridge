/**
 * @file config.h
 * @brief Configuration types for the Gateway Device SDK
 */

#pragma once

#include "types.h"
#include <string>
#include <chrono>
#include <functional>
#include <optional>

namespace gateway {

/**
 * @brief TLS/SSL configuration options
 */
struct TlsConfig {
    /// Enable TLS (automatically enabled for wss:// URLs)
    bool enabled = true;

    /// Verify server certificate (set to false only for development)
    bool verifyPeer = true;

    /// Path to CA certificate file (PEM format)
    std::string caCertPath;

    /// Path to client certificate file (for mutual TLS)
    std::string clientCertPath;

    /// Path to client private key file
    std::string clientKeyPath;

    /// Server name for SNI (defaults to host from URL)
    std::string serverName;
};

/**
 * @brief Reconnection policy configuration
 */
struct ReconnectConfig {
    /// Enable automatic reconnection
    bool enabled = true;

    /// Initial delay before first reconnect attempt
    Duration initialDelay{1000};

    /// Maximum delay between reconnect attempts
    Duration maxDelay{30000};

    /// Multiplier for exponential backoff (e.g., 2.0 = double each time)
    double backoffMultiplier = 2.0;

    /// Add random jitter to prevent thundering herd
    bool jitterEnabled = true;

    /// Maximum jitter as fraction of delay (0.0 to 1.0)
    double maxJitterFraction = 0.25;

    /// Maximum number of reconnect attempts (0 = unlimited)
    uint32_t maxAttempts = 0;

    /// Resubscribe to all subscriptions after reconnect
    bool resubscribeOnReconnect = true;
};

/**
 * @brief Heartbeat/ping configuration
 */
struct HeartbeatConfig {
    /// Enable heartbeat mechanism
    bool enabled = true;

    /// Interval between ping messages
    Duration interval{30000};

    /// Timeout waiting for pong response
    Duration timeout{10000};

    /// Number of missed pongs before considering connection dead
    uint32_t missedPongsBeforeDisconnect = 2;
};

/**
 * @brief Message buffer configuration
 */
struct BufferConfig {
    /// Maximum number of outgoing messages to buffer
    size_t maxOutgoingMessages = 1000;

    /// Maximum number of incoming messages to buffer
    size_t maxIncomingMessages = 1000;

    /// Maximum size of a single message payload (bytes)
    size_t maxPayloadSize = 1048576;  // 1MB - matches gateway MaxMessageSize
};

/**
 * @brief Logging configuration
 */
struct LogConfig {
    /// Enable logging
    bool enabled = true;

    /// Log level (0=trace, 1=debug, 2=info, 3=warn, 4=error, 5=fatal)
    int level = 2;  // Info

    /// Include timestamps in log output
    bool timestamps = true;

    /// Include thread ID in log output
    bool threadId = false;
};

/**
 * @brief Main configuration for the Gateway client
 *
 * Example usage:
 * @code
 * gateway::GatewayConfig config;
 * config.gatewayUrl = "wss://gateway.example.com/ws";
 * config.deviceId = "sensor-001";
 * config.authToken = "your-api-token";
 * config.deviceType = gateway::DeviceType::Sensor;
 *
 * gateway::GatewayClient client(config);
 * @endcode
 */
struct GatewayConfig {
    //-------------------------------------------------------------------------
    // Required settings
    //-------------------------------------------------------------------------

    /// Gateway WebSocket URL (e.g., "wss://gateway.example.com/ws")
    std::string gatewayUrl;

    /// Unique device identifier
    std::string deviceId;

    /// Authentication token/API key
    std::string authToken;

    //-------------------------------------------------------------------------
    // Device settings
    //-------------------------------------------------------------------------

    /// Type of device (sensor, actuator, controller, etc.)
    DeviceType deviceType = DeviceType::Sensor;

    /// Custom device type string (used when deviceType is Custom)
    std::string customDeviceType;

    //-------------------------------------------------------------------------
    // Connection settings
    //-------------------------------------------------------------------------

    /// Connection timeout
    Duration connectTimeout{10000};

    /// Authentication timeout (must complete auth within this time)
    Duration authTimeout{30000};

    /// Operation timeout for publish/subscribe
    Duration operationTimeout{5000};

    //-------------------------------------------------------------------------
    // Sub-configurations
    //-------------------------------------------------------------------------

    /// TLS configuration
    TlsConfig tls;

    /// Reconnection policy
    ReconnectConfig reconnect;

    /// Heartbeat/ping configuration
    HeartbeatConfig heartbeat;

    /// Buffer configuration
    BufferConfig buffer;

    /// Logging configuration
    LogConfig logging;

    //-------------------------------------------------------------------------
    // Validation
    //-------------------------------------------------------------------------

    /**
     * @brief Validate the configuration
     * @return true if configuration is valid
     */
    bool isValid() const {
        if (gatewayUrl.empty()) return false;
        if (deviceId.empty()) return false;
        if (authToken.empty()) return false;
        if (deviceId.length() > 256) return false;
        return true;
    }

    /**
     * @brief Get effective device type string
     */
    std::string getDeviceTypeString() const {
        if (deviceType == DeviceType::Custom && !customDeviceType.empty()) {
            return customDeviceType;
        }
        return deviceTypeToString(deviceType);
    }
};

/**
 * @brief Builder pattern for creating GatewayConfig
 *
 * Example:
 * @code
 * auto config = gateway::GatewayConfigBuilder()
 *     .gatewayUrl("wss://gateway.example.com/ws")
 *     .deviceId("sensor-001")
 *     .authToken("token123")
 *     .deviceType(gateway::DeviceType::Sensor)
 *     .enableReconnect(true, 5000, 60000)
 *     .enableHeartbeat(30000)
 *     .build();
 * @endcode
 */
class GatewayConfigBuilder {
public:
    GatewayConfigBuilder& gatewayUrl(const std::string& url) {
        config_.gatewayUrl = url;
        return *this;
    }

    GatewayConfigBuilder& deviceId(const std::string& id) {
        config_.deviceId = id;
        return *this;
    }

    GatewayConfigBuilder& authToken(const std::string& token) {
        config_.authToken = token;
        return *this;
    }

    GatewayConfigBuilder& deviceType(DeviceType type) {
        config_.deviceType = type;
        return *this;
    }

    GatewayConfigBuilder& customDeviceType(const std::string& type) {
        config_.deviceType = DeviceType::Custom;
        config_.customDeviceType = type;
        return *this;
    }

    GatewayConfigBuilder& connectTimeout(Duration timeout) {
        config_.connectTimeout = timeout;
        return *this;
    }

    GatewayConfigBuilder& authTimeout(Duration timeout) {
        config_.authTimeout = timeout;
        return *this;
    }

    GatewayConfigBuilder& operationTimeout(Duration timeout) {
        config_.operationTimeout = timeout;
        return *this;
    }

    GatewayConfigBuilder& enableTls(bool verify = true) {
        config_.tls.enabled = true;
        config_.tls.verifyPeer = verify;
        return *this;
    }

    GatewayConfigBuilder& tlsCertificates(
        const std::string& caCert,
        const std::string& clientCert = "",
        const std::string& clientKey = ""
    ) {
        config_.tls.caCertPath = caCert;
        config_.tls.clientCertPath = clientCert;
        config_.tls.clientKeyPath = clientKey;
        return *this;
    }

    GatewayConfigBuilder& enableReconnect(
        bool enable = true,
        Duration initialDelay = Duration{1000},
        Duration maxDelay = Duration{30000}
    ) {
        config_.reconnect.enabled = enable;
        config_.reconnect.initialDelay = initialDelay;
        config_.reconnect.maxDelay = maxDelay;
        return *this;
    }

    GatewayConfigBuilder& maxReconnectAttempts(uint32_t attempts) {
        config_.reconnect.maxAttempts = attempts;
        return *this;
    }

    GatewayConfigBuilder& enableHeartbeat(Duration interval = Duration{30000}) {
        config_.heartbeat.enabled = true;
        config_.heartbeat.interval = interval;
        return *this;
    }

    GatewayConfigBuilder& disableHeartbeat() {
        config_.heartbeat.enabled = false;
        return *this;
    }

    GatewayConfigBuilder& bufferSize(size_t outgoing, size_t incoming = 0) {
        config_.buffer.maxOutgoingMessages = outgoing;
        config_.buffer.maxIncomingMessages = incoming > 0 ? incoming : outgoing;
        return *this;
    }

    GatewayConfigBuilder& maxPayloadSize(size_t size) {
        config_.buffer.maxPayloadSize = size;
        return *this;
    }

    GatewayConfigBuilder& logLevel(int level) {
        config_.logging.level = level;
        return *this;
    }

    GatewayConfigBuilder& disableLogging() {
        config_.logging.enabled = false;
        return *this;
    }

    GatewayConfig build() const {
        return config_;
    }

private:
    GatewayConfig config_;
};

} // namespace gateway
