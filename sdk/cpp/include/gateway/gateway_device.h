/**
 * @file gateway_device.h
 * @brief Main public API for the Gateway Device SDK
 *
 * This is the primary header file that device manufacturers should include.
 * It provides a simple, high-level API for connecting devices to the
 * NATS WebSocket Bridge Gateway.
 *
 * @example
 * @code
 * #include <gateway/gateway_device.h>
 *
 * int main() {
 *     // Configure the client
 *     gateway::GatewayConfig config;
 *     config.gatewayUrl = "wss://gateway.example.com/ws";
 *     config.deviceId = "sensor-001";
 *     config.authToken = "your-api-token";
 *     config.deviceType = gateway::DeviceType::Sensor;
 *
 *     // Create and connect
 *     gateway::GatewayClient client(config);
 *
 *     if (!client.connect()) {
 *         std::cerr << "Failed to connect" << std::endl;
 *         return 1;
 *     }
 *
 *     // Subscribe to commands
 *     client.subscribe("commands.>", [](const std::string& subject,
 *                                        const gateway::JsonValue& payload,
 *                                        const gateway::Message& msg) {
 *         std::cout << "Received command on " << subject << std::endl;
 *     });
 *
 *     // Publish sensor data
 *     gateway::JsonValue data;
 *     data["temperature"] = 25.5;
 *     data["humidity"] = 60.0;
 *
 *     client.publish("sensors.temperature", data);
 *
 *     // Run event loop
 *     while (client.isConnected()) {
 *         client.poll();
 *     }
 *
 *     return 0;
 * }
 * @endcode
 */

#pragma once

// Include all SDK headers
#include "types.h"
#include "error.h"
#include "config.h"
#include "message.h"
#include "logger.h"

#include <memory>
#include <functional>
#include <string>
#include <vector>
#include <atomic>
#include <thread>
#include <mutex>

namespace gateway {

// Forward declarations
class ITransport;
class AuthManager;
class ReconnectPolicy;

/**
 * @brief Event callbacks for connection lifecycle
 */
struct ClientCallbacks {
    /// Called when connection is established and authenticated
    std::function<void()> onConnected;

    /// Called when disconnected (with reason)
    std::function<void(ErrorCode code, const std::string& reason)> onDisconnected;

    /// Called when reconnecting
    std::function<void(uint32_t attempt)> onReconnecting;

    /// Called when an error occurs
    std::function<void(ErrorCode code, const std::string& message)> onError;

    /// Called when connection state changes
    std::function<void(ConnectionState oldState, ConnectionState newState)> onStateChanged;
};

/**
 * @brief Statistics about the client connection
 */
struct ClientStats {
    uint64_t messagesSent = 0;
    uint64_t messagesReceived = 0;
    uint64_t bytesSent = 0;
    uint64_t bytesReceived = 0;
    uint32_t reconnectCount = 0;
    uint32_t errorCount = 0;
    Timestamp connectedAt;
    Timestamp lastActivityAt;
    Duration totalConnectedTime{0};
};

/**
 * @brief Main client class for connecting devices to the gateway
 *
 * This is the primary class that device manufacturers will use.
 * It provides a simple, high-level API for:
 * - Connecting to the gateway with authentication
 * - Publishing messages to subjects
 * - Subscribing to subjects
 * - Automatic reconnection with backoff
 * - Heartbeat/keep-alive management
 *
 * Thread Safety:
 * - All public methods are thread-safe
 * - Callbacks are invoked from the polling thread
 * - Use poll() in a dedicated thread or event loop
 */
class GatewayClient {
public:
    /**
     * @brief Create a new gateway client
     * @param config Client configuration
     */
    explicit GatewayClient(const GatewayConfig& config);

    /**
     * @brief Create a client with a custom logger
     * @param config Client configuration
     * @param logger Custom logger instance
     */
    GatewayClient(const GatewayConfig& config, std::shared_ptr<Logger> logger);

    /**
     * @brief Destructor - disconnects if connected
     */
    ~GatewayClient();

    // Non-copyable
    GatewayClient(const GatewayClient&) = delete;
    GatewayClient& operator=(const GatewayClient&) = delete;

    // Movable
    GatewayClient(GatewayClient&&) noexcept;
    GatewayClient& operator=(GatewayClient&&) noexcept;

    //-------------------------------------------------------------------------
    // Connection Management
    //-------------------------------------------------------------------------

    /**
     * @brief Connect to the gateway
     *
     * Establishes WebSocket connection and performs authentication.
     * This is a blocking call that returns when connected or on error.
     *
     * @return true if connected and authenticated successfully
     */
    bool connect();

    /**
     * @brief Connect asynchronously
     *
     * Starts connection in background. Use callbacks or poll isConnected().
     *
     * @return Result indicating if connection attempt started
     */
    Result<void> connectAsync();

    /**
     * @brief Disconnect from the gateway
     *
     * Gracefully closes the connection. Does not trigger reconnection.
     */
    void disconnect();

    /**
     * @brief Check if currently connected and authenticated
     */
    bool isConnected() const;

    /**
     * @brief Get current connection state
     */
    ConnectionState getState() const;

    /**
     * @brief Get device info (available after authentication)
     */
    const std::optional<DeviceInfo>& getDeviceInfo() const;

    //-------------------------------------------------------------------------
    // Publishing
    //-------------------------------------------------------------------------

    /**
     * @brief Publish a message to a subject
     *
     * @param subject NATS subject to publish to
     * @param payload JSON payload
     * @return Result indicating success or failure
     */
    Result<void> publish(const std::string& subject, const JsonValue& payload);

    /**
     * @brief Publish a raw string payload
     *
     * @param subject NATS subject
     * @param payload String payload (will be wrapped in JSON)
     * @return Result indicating success or failure
     */
    Result<void> publish(const std::string& subject, const std::string& payload);

    /**
     * @brief Publish with QoS setting
     *
     * @param subject NATS subject
     * @param payload JSON payload
     * @param qos Quality of service level
     * @return Result indicating success or failure
     */
    Result<void> publish(const std::string& subject, const JsonValue& payload, QoS qos);

    //-------------------------------------------------------------------------
    // Subscribing
    //-------------------------------------------------------------------------

    /**
     * @brief Subscribe to a subject
     *
     * @param subject NATS subject pattern (supports wildcards * and >)
     * @param handler Callback for received messages
     * @return Subscription ID on success, or error
     */
    Result<SubscriptionId> subscribe(const std::string& subject, SubscriptionHandler handler);

    /**
     * @brief Subscribe with MessageHandler (receives full Message)
     *
     * @param subject NATS subject pattern
     * @param handler Callback receiving full Message object
     * @return Subscription ID on success, or error
     */
    Result<SubscriptionId> subscribe(const std::string& subject, MessageHandler handler);

    /**
     * @brief Unsubscribe from a subject
     *
     * @param subscriptionId Subscription to cancel
     * @return Result indicating success or failure
     */
    Result<void> unsubscribe(SubscriptionId subscriptionId);

    /**
     * @brief Unsubscribe by subject
     *
     * @param subject Subject to unsubscribe from
     * @return Result indicating success or failure
     */
    Result<void> unsubscribe(const std::string& subject);

    /**
     * @brief Get list of active subscriptions
     */
    std::vector<std::string> getSubscriptions() const;

    //-------------------------------------------------------------------------
    // Event Loop
    //-------------------------------------------------------------------------

    /**
     * @brief Process events (call regularly in your main loop)
     *
     * This method:
     * - Processes incoming messages
     * - Sends outgoing messages
     * - Handles heartbeats
     * - Manages reconnection
     *
     * @param timeout Maximum time to wait for events (default: 100ms)
     */
    void poll(Duration timeout = Duration{100});

    /**
     * @brief Run the event loop (blocking)
     *
     * Runs until disconnect() is called or connection is lost
     * without successful reconnection.
     */
    void run();

    /**
     * @brief Run the event loop in a background thread
     *
     * @return true if thread started successfully
     */
    bool runAsync();

    /**
     * @brief Stop the async event loop
     */
    void stop();

    //-------------------------------------------------------------------------
    // Callbacks
    //-------------------------------------------------------------------------

    /**
     * @brief Set event callbacks
     */
    void setCallbacks(const ClientCallbacks& callbacks);

    /**
     * @brief Set connected callback
     */
    void onConnected(std::function<void()> callback);

    /**
     * @brief Set disconnected callback
     */
    void onDisconnected(std::function<void(ErrorCode, const std::string&)> callback);

    /**
     * @brief Set error callback
     */
    void onError(std::function<void(ErrorCode, const std::string&)> callback);

    /**
     * @brief Set reconnecting callback
     */
    void onReconnecting(std::function<void(uint32_t)> callback);

    //-------------------------------------------------------------------------
    // Statistics & Diagnostics
    //-------------------------------------------------------------------------

    /**
     * @brief Get client statistics
     */
    ClientStats getStats() const;

    /**
     * @brief Get the logger instance
     */
    Logger& getLogger();

    /**
     * @brief Get SDK version string
     */
    static const char* getVersion();

    /**
     * @brief Get protocol version string
     */
    static const char* getProtocolVersion();

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

/**
 * @brief Convenience function to create a configured client
 *
 * @param url Gateway URL
 * @param deviceId Device identifier
 * @param token Authentication token
 * @param type Device type
 * @return Configured GatewayClient
 */
inline GatewayClient createClient(
    const std::string& url,
    const std::string& deviceId,
    const std::string& token,
    DeviceType type = DeviceType::Sensor
) {
    return GatewayClient(
        GatewayConfigBuilder()
            .gatewayUrl(url)
            .deviceId(deviceId)
            .authToken(token)
            .deviceType(type)
            .build()
    );
}

} // namespace gateway
