#include "gateway/metrics.h"
#include <atomic>
#include <iostream>
#include <iomanip>
#include <mutex>
#include <sstream>

namespace gateway {

// ============================================================================
// LoggingMetricsCallback Implementation
// ============================================================================

namespace {
std::string timestamp() {
    auto now = std::chrono::system_clock::now();
    auto time = std::chrono::system_clock::to_time_t(now);
    std::ostringstream oss;
    oss << std::put_time(std::localtime(&time), "%Y-%m-%d %H:%M:%S");
    return oss.str();
}
}

void LoggingMetricsCallback::onConnectionOpened() {
    std::cout << "[" << timestamp() << "] [METRICS] Connection opened" << std::endl;
}

void LoggingMetricsCallback::onConnectionClosed(const std::string& reason) {
    std::cout << "[" << timestamp() << "] [METRICS] Connection closed: " << reason << std::endl;
}

void LoggingMetricsCallback::onReconnectAttempt(uint32_t attemptNumber, uint32_t delayMs) {
    std::cout << "[" << timestamp() << "] [METRICS] Reconnect attempt " << attemptNumber
              << " in " << delayMs << "ms" << std::endl;
}

void LoggingMetricsCallback::onConnectionDuration(double durationMs) {
    std::cout << "[" << timestamp() << "] [METRICS] Connection duration: "
              << std::fixed << std::setprecision(2) << durationMs << "ms" << std::endl;
}

void LoggingMetricsCallback::onAuthentication(bool success, double durationMs) {
    std::cout << "[" << timestamp() << "] [METRICS] Authentication "
              << (success ? "succeeded" : "failed") << " in "
              << std::fixed << std::setprecision(2) << durationMs << "ms" << std::endl;
}

void LoggingMetricsCallback::onMessagePublished(const std::string& subject,
                                                size_t sizeBytes,
                                                double latencyMs) {
    std::cout << "[" << timestamp() << "] [METRICS] Published to " << subject
              << " (" << sizeBytes << " bytes, "
              << std::fixed << std::setprecision(2) << latencyMs << "ms)" << std::endl;
}

void LoggingMetricsCallback::onPublishError(const std::string& subject, int errorCode) {
    std::cout << "[" << timestamp() << "] [METRICS] Publish error on " << subject
              << " (code " << errorCode << ")" << std::endl;
}

void LoggingMetricsCallback::onMessageReceived(const std::string& subject, size_t sizeBytes) {
    std::cout << "[" << timestamp() << "] [METRICS] Received on " << subject
              << " (" << sizeBytes << " bytes)" << std::endl;
}

void LoggingMetricsCallback::onSubscriptionCreated(const std::string& subject) {
    std::cout << "[" << timestamp() << "] [METRICS] Subscribed to " << subject << std::endl;
}

void LoggingMetricsCallback::onSubscriptionRemoved(const std::string& subject) {
    std::cout << "[" << timestamp() << "] [METRICS] Unsubscribed from " << subject << std::endl;
}

void LoggingMetricsCallback::onBufferEnqueue(size_t currentSize, size_t maxSize) {
    std::cout << "[" << timestamp() << "] [METRICS] Buffer: "
              << currentSize << "/" << maxSize << std::endl;
}

void LoggingMetricsCallback::onBufferOverflow() {
    std::cout << "[" << timestamp() << "] [METRICS] Buffer overflow!" << std::endl;
}

void LoggingMetricsCallback::onPingSent() {
    std::cout << "[" << timestamp() << "] [METRICS] Ping sent" << std::endl;
}

void LoggingMetricsCallback::onPongReceived(double roundTripMs) {
    std::cout << "[" << timestamp() << "] [METRICS] Pong received (RTT: "
              << std::fixed << std::setprecision(2) << roundTripMs << "ms)" << std::endl;
}

void LoggingMetricsCallback::onHeartbeatTimeout() {
    std::cout << "[" << timestamp() << "] [METRICS] Heartbeat timeout!" << std::endl;
}

void LoggingMetricsCallback::onError(int errorCode, const std::string& message) {
    std::cout << "[" << timestamp() << "] [METRICS] Error " << errorCode
              << ": " << message << std::endl;
}

// ============================================================================
// AggregatingMetricsCallback Implementation
// ============================================================================

struct AggregatingMetricsCallback::Impl {
    mutable std::mutex mutex;

    // Counters
    std::atomic<uint64_t> totalConnections{0};
    std::atomic<uint64_t> successfulConnections{0};
    std::atomic<uint64_t> failedConnections{0};
    std::atomic<uint64_t> reconnectAttempts{0};
    std::atomic<uint64_t> disconnections{0};

    std::atomic<uint64_t> messagesPublished{0};
    std::atomic<uint64_t> messagesReceived{0};
    std::atomic<uint64_t> publishErrors{0};
    std::atomic<uint64_t> bytesPublished{0};
    std::atomic<uint64_t> bytesReceived{0};

    std::atomic<uint64_t> bufferOverflows{0};

    // Non-atomic values (protected by mutex)
    double lastConnectDurationMs{0};
    double lastAuthDurationMs{0};
    double totalPublishLatencyMs{0};
    uint64_t publishLatencyCount{0};

    uint32_t currentBufferSize{0};
    uint32_t maxBufferSize{0};

    bool isConnected{false};
    std::chrono::steady_clock::time_point connectionStartTime;
};

AggregatingMetricsCallback::AggregatingMetricsCallback()
    : impl_(std::make_unique<Impl>()) {}

AggregatingMetricsCallback::~AggregatingMetricsCallback() = default;

ClientStats AggregatingMetricsCallback::getStats() const {
    std::lock_guard<std::mutex> lock(impl_->mutex);

    ClientStats stats;
    stats.totalConnections = impl_->totalConnections.load();
    stats.successfulConnections = impl_->successfulConnections.load();
    stats.failedConnections = impl_->failedConnections.load();
    stats.reconnectAttempts = impl_->reconnectAttempts.load();
    stats.disconnections = impl_->disconnections.load();

    stats.messagesPublished = impl_->messagesPublished.load();
    stats.messagesReceived = impl_->messagesReceived.load();
    stats.publishErrors = impl_->publishErrors.load();
    stats.bytesPublished = impl_->bytesPublished.load();
    stats.bytesReceived = impl_->bytesReceived.load();

    stats.lastConnectDurationMs = impl_->lastConnectDurationMs;
    stats.lastAuthDurationMs = impl_->lastAuthDurationMs;

    if (impl_->publishLatencyCount > 0) {
        stats.avgPublishLatencyMs = impl_->totalPublishLatencyMs / impl_->publishLatencyCount;
    }

    stats.currentBufferSize = impl_->currentBufferSize;
    stats.maxBufferSize = impl_->maxBufferSize;
    stats.bufferOverflows = impl_->bufferOverflows.load();

    stats.isConnected = impl_->isConnected;
    if (impl_->isConnected) {
        auto now = std::chrono::steady_clock::now();
        stats.connectedDurationMs = std::chrono::duration_cast<std::chrono::milliseconds>(
            now - impl_->connectionStartTime).count();
    }

    stats.timestamp = std::chrono::steady_clock::now();

    return stats;
}

void AggregatingMetricsCallback::reset() {
    std::lock_guard<std::mutex> lock(impl_->mutex);

    impl_->totalConnections = 0;
    impl_->successfulConnections = 0;
    impl_->failedConnections = 0;
    impl_->reconnectAttempts = 0;
    impl_->disconnections = 0;

    impl_->messagesPublished = 0;
    impl_->messagesReceived = 0;
    impl_->publishErrors = 0;
    impl_->bytesPublished = 0;
    impl_->bytesReceived = 0;

    impl_->bufferOverflows = 0;

    impl_->lastConnectDurationMs = 0;
    impl_->lastAuthDurationMs = 0;
    impl_->totalPublishLatencyMs = 0;
    impl_->publishLatencyCount = 0;

    impl_->currentBufferSize = 0;
    impl_->maxBufferSize = 0;
}

void AggregatingMetricsCallback::onConnectionOpened() {
    impl_->totalConnections++;
    impl_->successfulConnections++;

    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->isConnected = true;
    impl_->connectionStartTime = std::chrono::steady_clock::now();
}

void AggregatingMetricsCallback::onConnectionClosed(const std::string& /*reason*/) {
    impl_->disconnections++;

    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->isConnected = false;
}

void AggregatingMetricsCallback::onReconnectAttempt(uint32_t /*attemptNumber*/,
                                                    uint32_t /*delayMs*/) {
    impl_->reconnectAttempts++;
}

void AggregatingMetricsCallback::onConnectionDuration(double durationMs) {
    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->lastConnectDurationMs = durationMs;
}

void AggregatingMetricsCallback::onAuthentication(bool success, double durationMs) {
    if (!success) {
        impl_->failedConnections++;
    }

    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->lastAuthDurationMs = durationMs;
}

void AggregatingMetricsCallback::onMessagePublished(const std::string& /*subject*/,
                                                    size_t sizeBytes,
                                                    double latencyMs) {
    impl_->messagesPublished++;
    impl_->bytesPublished += sizeBytes;

    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->totalPublishLatencyMs += latencyMs;
    impl_->publishLatencyCount++;
}

void AggregatingMetricsCallback::onPublishError(const std::string& /*subject*/,
                                                int /*errorCode*/) {
    impl_->publishErrors++;
}

void AggregatingMetricsCallback::onMessageReceived(const std::string& /*subject*/,
                                                   size_t sizeBytes) {
    impl_->messagesReceived++;
    impl_->bytesReceived += sizeBytes;
}

void AggregatingMetricsCallback::onBufferEnqueue(size_t currentSize, size_t /*maxSize*/) {
    std::lock_guard<std::mutex> lock(impl_->mutex);
    impl_->currentBufferSize = static_cast<uint32_t>(currentSize);
    if (currentSize > impl_->maxBufferSize) {
        impl_->maxBufferSize = static_cast<uint32_t>(currentSize);
    }
}

void AggregatingMetricsCallback::onBufferOverflow() {
    impl_->bufferOverflows++;
}

void AggregatingMetricsCallback::onPongReceived(double /*roundTripMs*/) {
    // Could track RTT stats here if needed
}

void AggregatingMetricsCallback::onError(int /*errorCode*/, const std::string& /*message*/) {
    // Could track error counts by code here if needed
}

} // namespace gateway
