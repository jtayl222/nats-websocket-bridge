#pragma once

#include <chrono>
#include <cstdint>
#include <functional>
#include <string>

namespace gateway {

/**
 * @brief Statistics snapshot from the SDK client
 *
 * This structure contains current metrics that can be retrieved
 * at any time via GatewayClient::getStats()
 */
struct ClientStats {
    // Connection metrics
    uint64_t totalConnections{0};        ///< Total connection attempts
    uint64_t successfulConnections{0};   ///< Successful connections
    uint64_t failedConnections{0};       ///< Failed connection attempts
    uint64_t reconnectAttempts{0};       ///< Number of reconnection attempts
    uint64_t disconnections{0};          ///< Number of disconnections

    // Message metrics
    uint64_t messagesPublished{0};       ///< Total messages published
    uint64_t messagesReceived{0};        ///< Total messages received
    uint64_t publishErrors{0};           ///< Failed publish attempts
    uint64_t bytesPublished{0};          ///< Total bytes published
    uint64_t bytesReceived{0};           ///< Total bytes received

    // Timing metrics (in milliseconds)
    double lastConnectDurationMs{0};     ///< Last connection duration
    double lastAuthDurationMs{0};        ///< Last authentication duration
    double avgPublishLatencyMs{0};       ///< Average publish latency

    // Buffer metrics
    uint32_t currentBufferSize{0};       ///< Current pending messages in buffer
    uint32_t maxBufferSize{0};           ///< Maximum buffer size reached
    uint64_t bufferOverflows{0};         ///< Messages dropped due to full buffer

    // State
    bool isConnected{false};             ///< Current connection state
    int64_t connectedDurationMs{0};      ///< How long connected (if connected)

    // Timestamp
    std::chrono::steady_clock::time_point timestamp;  ///< When stats were captured
};

/**
 * @brief Metrics callback interface for SDK instrumentation
 *
 * Implement this interface to receive real-time metrics callbacks
 * from the SDK. This allows integration with external monitoring
 * systems like Prometheus, StatsD, or custom metrics collectors.
 *
 * Usage:
 * @code
 * class MyMetrics : public MetricsCallback {
 * public:
 *     void onConnectionOpened() override {
 *         // Increment connection counter
 *         prometheus_counter_inc(connections_total);
 *     }
 *     // ... implement other methods
 * };
 *
 * auto metrics = std::make_shared<MyMetrics>();
 * client.setMetricsCallback(metrics);
 * @endcode
 */
class MetricsCallback {
public:
    virtual ~MetricsCallback() = default;

    // ====== Connection Events ======

    /**
     * @brief Called when a WebSocket connection is established
     */
    virtual void onConnectionOpened() {}

    /**
     * @brief Called when a connection is closed
     * @param reason Reason for disconnection (normal, error, timeout, etc.)
     */
    virtual void onConnectionClosed(const std::string& reason) {}

    /**
     * @brief Called when a reconnection attempt starts
     * @param attemptNumber Which attempt this is (1-based)
     * @param delayMs Delay before this attempt in milliseconds
     */
    virtual void onReconnectAttempt(uint32_t attemptNumber, uint32_t delayMs) {}

    /**
     * @brief Called to record connection duration
     * @param durationMs Duration of the connection in milliseconds
     */
    virtual void onConnectionDuration(double durationMs) {}

    // ====== Authentication Events ======

    /**
     * @brief Called when authentication completes
     * @param success Whether authentication succeeded
     * @param durationMs Time taken for authentication
     */
    virtual void onAuthentication(bool success, double durationMs) {}

    // ====== Message Events ======

    /**
     * @brief Called when a message is published
     * @param subject The subject the message was published to
     * @param sizeBytes Size of the message in bytes
     * @param latencyMs Time from publish call to completion
     */
    virtual void onMessagePublished(const std::string& subject,
                                    size_t sizeBytes,
                                    double latencyMs) {}

    /**
     * @brief Called when a publish fails
     * @param subject The subject that failed
     * @param errorCode The error code
     */
    virtual void onPublishError(const std::string& subject, int errorCode) {}

    /**
     * @brief Called when a message is received
     * @param subject The subject the message was received on
     * @param sizeBytes Size of the message in bytes
     */
    virtual void onMessageReceived(const std::string& subject, size_t sizeBytes) {}

    /**
     * @brief Called when a subscription is created
     * @param subject The subject subscribed to
     */
    virtual void onSubscriptionCreated(const std::string& subject) {}

    /**
     * @brief Called when a subscription is removed
     * @param subject The subject unsubscribed from
     */
    virtual void onSubscriptionRemoved(const std::string& subject) {}

    // ====== Buffer Events ======

    /**
     * @brief Called when a message is added to the outgoing buffer
     * @param currentSize Current number of messages in buffer
     * @param maxSize Maximum buffer capacity
     */
    virtual void onBufferEnqueue(size_t currentSize, size_t maxSize) {}

    /**
     * @brief Called when a message is dropped due to full buffer
     */
    virtual void onBufferOverflow() {}

    // ====== Heartbeat Events ======

    /**
     * @brief Called when a ping is sent
     */
    virtual void onPingSent() {}

    /**
     * @brief Called when a pong is received
     * @param roundTripMs Round-trip time in milliseconds
     */
    virtual void onPongReceived(double roundTripMs) {}

    /**
     * @brief Called when a heartbeat times out
     */
    virtual void onHeartbeatTimeout() {}

    // ====== Error Events ======

    /**
     * @brief Called when any error occurs
     * @param errorCode The error code
     * @param message Error message
     */
    virtual void onError(int errorCode, const std::string& message) {}
};

/**
 * @brief Simple logging metrics callback for debugging
 *
 * Logs all metrics events to stdout. Useful for development and debugging.
 */
class LoggingMetricsCallback : public MetricsCallback {
public:
    void onConnectionOpened() override;
    void onConnectionClosed(const std::string& reason) override;
    void onReconnectAttempt(uint32_t attemptNumber, uint32_t delayMs) override;
    void onConnectionDuration(double durationMs) override;
    void onAuthentication(bool success, double durationMs) override;
    void onMessagePublished(const std::string& subject, size_t sizeBytes, double latencyMs) override;
    void onPublishError(const std::string& subject, int errorCode) override;
    void onMessageReceived(const std::string& subject, size_t sizeBytes) override;
    void onSubscriptionCreated(const std::string& subject) override;
    void onSubscriptionRemoved(const std::string& subject) override;
    void onBufferEnqueue(size_t currentSize, size_t maxSize) override;
    void onBufferOverflow() override;
    void onPingSent() override;
    void onPongReceived(double roundTripMs) override;
    void onHeartbeatTimeout() override;
    void onError(int errorCode, const std::string& message) override;
};

/**
 * @brief Aggregating metrics callback
 *
 * Collects metrics in memory for periodic retrieval.
 * Thread-safe for concurrent access.
 */
class AggregatingMetricsCallback : public MetricsCallback {
public:
    AggregatingMetricsCallback();
    ~AggregatingMetricsCallback() override;

    // Get current aggregated stats
    ClientStats getStats() const;

    // Reset all counters
    void reset();

    // MetricsCallback implementation
    void onConnectionOpened() override;
    void onConnectionClosed(const std::string& reason) override;
    void onReconnectAttempt(uint32_t attemptNumber, uint32_t delayMs) override;
    void onConnectionDuration(double durationMs) override;
    void onAuthentication(bool success, double durationMs) override;
    void onMessagePublished(const std::string& subject, size_t sizeBytes, double latencyMs) override;
    void onPublishError(const std::string& subject, int errorCode) override;
    void onMessageReceived(const std::string& subject, size_t sizeBytes) override;
    void onBufferEnqueue(size_t currentSize, size_t maxSize) override;
    void onBufferOverflow() override;
    void onPongReceived(double roundTripMs) override;
    void onError(int errorCode, const std::string& message) override;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace gateway
