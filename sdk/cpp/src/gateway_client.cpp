/**
 * @file gateway_client.cpp
 * @brief Main GatewayClient implementation
 */

#include "gateway/gateway_device.h"
#include "gateway/transport.h"
#include "gateway/protocol.h"
#include "gateway/auth.h"
#include "gateway/reconnect_policy.h"

#include <thread>
#include <atomic>
#include <mutex>
#include <condition_variable>
#include <map>
#include <queue>

namespace gateway {

class GatewayClient::Impl {
public:
    Impl(const GatewayConfig& config, std::shared_ptr<Logger> logger)
        : config_(config)
        , logger_(logger ? logger : std::make_shared<ConsoleLogger>(config.logging))
        , state_(ConnectionState::Disconnected)
        , reconnectPolicy_(config.reconnect)
        , nextSubscriptionId_(1)
        , running_(false)
    {
        transport_ = createTransport(config.tls, *logger_);
        setupTransportCallbacks();
    }

    ~Impl() {
        stop();
        disconnect();
    }

    bool connect() {
        std::unique_lock<std::mutex> lock(mutex_);

        if (state_ == ConnectionState::Connected) {
            return true;
        }

        if (state_ == ConnectionState::Connecting ||
            state_ == ConnectionState::Authenticating) {
            return false;
        }

        setState(ConnectionState::Connecting);
        lock.unlock();

        // Connect transport
        auto result = transport_->connect(config_.gatewayUrl, config_.connectTimeout);
        if (result.failed()) {
            logger_->error("Client", "Connection failed: " + result.errorMessage());
            setState(ConnectionState::Disconnected);
            return false;
        }

        // Wait for connection established callback
        {
            std::unique_lock<std::mutex> connLock(mutex_);
            if (!connectCv_.wait_for(connLock, config_.connectTimeout,
                                    [this] { return state_ == ConnectionState::Authenticating ||
                                                    state_ == ConnectionState::Disconnected; })) {
                logger_->error("Client", "Connection timeout");
                transport_->disconnect(1000, "Connection timeout");
                setState(ConnectionState::Disconnected);
                return false;
            }

            if (state_ == ConnectionState::Disconnected) {
                return false;
            }
        }

        // Perform authentication
        return doAuthentication();
    }

    Result<void> connectAsync() {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ == ConnectionState::Connected ||
            state_ == ConnectionState::Connecting ||
            state_ == ConnectionState::Authenticating) {
            return Result<void>(ErrorCode::AlreadyConnected, "Already connected or connecting");
        }

        setState(ConnectionState::Connecting);

        auto result = transport_->connect(config_.gatewayUrl, config_.connectTimeout);
        if (result.failed()) {
            setState(ConnectionState::Disconnected);
            return result;
        }

        return Result<void>();
    }

    void disconnect() {
        {
            std::lock_guard<std::mutex> lock(mutex_);

            if (state_ == ConnectionState::Disconnected ||
                state_ == ConnectionState::Closed) {
                return;
            }

            reconnectPolicy_.setEnabled(false);  // Prevent auto-reconnect
            setState(ConnectionState::Closing);
        }

        transport_->disconnect(1000, "Client disconnect");
        authManager_.reset();

        setState(ConnectionState::Closed);
    }

    bool isConnected() const {
        return state_ == ConnectionState::Connected;
    }

    ConnectionState getState() const {
        return state_.load();
    }

    const std::optional<DeviceInfo>& getDeviceInfo() const {
        return authManager_.getDeviceInfo();
    }

    Result<void> publish(const std::string& subject, const JsonValue& payload) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ != ConnectionState::Connected) {
            return Result<void>(ErrorCode::NotConnected, "Not connected");
        }

        if (!Protocol::isValidSubject(subject)) {
            return Result<void>(ErrorCode::InvalidSubject, "Invalid subject: " + subject);
        }

        // Check authorization
        if (!authManager_.canPublish(subject)) {
            logger_->warn("Client", "Publish not authorized: " + subject);
            // Note: We let the gateway enforce this, just log a warning
        }

        Message msg = Message::publish(subject, payload);
        std::string json = Protocol::serialize(msg);

        auto result = transport_->send(json);
        if (result.ok()) {
            stats_.messagesSent++;
            stats_.bytesSent += json.size();
            stats_.lastActivityAt = std::chrono::system_clock::now();
        }

        return result;
    }

    Result<void> publish(const std::string& subject, const std::string& payload) {
        return publish(subject, JsonValue(payload));
    }

    Result<void> publish(const std::string& subject, const JsonValue& payload, QoS) {
        // QoS is handled by JetStream on the gateway side
        // We just publish normally
        return publish(subject, payload);
    }

    Result<SubscriptionId> subscribe(const std::string& subject, SubscriptionHandler handler) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ != ConnectionState::Connected) {
            return Result<SubscriptionId>(ErrorCode::NotConnected, "Not connected");
        }

        if (!Protocol::isValidSubject(subject)) {
            return Result<SubscriptionId>(ErrorCode::InvalidSubject, "Invalid subject");
        }

        // Create subscription
        SubscriptionId id = nextSubscriptionId_++;

        Subscription sub;
        sub.id = id;
        sub.subject = subject;
        sub.handler = std::move(handler);
        sub.active = true;

        subscriptions_[id] = std::move(sub);
        subjectToId_[subject] = id;

        // Send subscribe message
        Message msg = Message::subscribe(subject);
        std::string json = Protocol::serialize(msg);

        auto result = transport_->send(json);
        if (result.failed()) {
            subscriptions_.erase(id);
            subjectToId_.erase(subject);
            return Result<SubscriptionId>(result.error(), result.errorMessage());
        }

        logger_->info("Client", "Subscribed to: " + subject);
        return Result<SubscriptionId>(id);
    }

    Result<SubscriptionId> subscribe(const std::string& subject, MessageHandler handler) {
        return subscribe(subject, [h = std::move(handler)](
            const std::string&, const JsonValue&, const Message& msg) {
            h(msg);
        });
    }

    Result<void> unsubscribe(SubscriptionId id) {
        std::lock_guard<std::mutex> lock(mutex_);

        auto it = subscriptions_.find(id);
        if (it == subscriptions_.end()) {
            return Result<void>(ErrorCode::NotSubscribed, "Subscription not found");
        }

        std::string subject = it->second.subject;

        // Send unsubscribe message
        if (state_ == ConnectionState::Connected) {
            Message msg = Message::unsubscribe(subject);
            transport_->send(Protocol::serialize(msg));
        }

        subjectToId_.erase(subject);
        subscriptions_.erase(it);

        logger_->info("Client", "Unsubscribed from: " + subject);
        return Result<void>();
    }

    Result<void> unsubscribe(const std::string& subject) {
        std::lock_guard<std::mutex> lock(mutex_);

        auto it = subjectToId_.find(subject);
        if (it == subjectToId_.end()) {
            return Result<void>(ErrorCode::NotSubscribed, "Not subscribed to: " + subject);
        }

        SubscriptionId id = it->second;
        lock.unlock();

        return unsubscribe(id);
    }

    std::vector<std::string> getSubscriptions() const {
        std::lock_guard<std::mutex> lock(mutex_);

        std::vector<std::string> result;
        for (const auto& [id, sub] : subscriptions_) {
            if (sub.active) {
                result.push_back(sub.subject);
            }
        }
        return result;
    }

    void poll(Duration timeout) {
        transport_->poll(timeout);

        // Process heartbeat
        if (state_ == ConnectionState::Connected && config_.heartbeat.enabled) {
            processHeartbeat();
        }

        // Handle reconnection
        if (state_ == ConnectionState::Reconnecting) {
            processReconnection();
        }
    }

    void run() {
        running_ = true;

        while (running_ && state_ != ConnectionState::Closed) {
            poll(Duration{100});
        }
    }

    bool runAsync() {
        if (asyncThread_.joinable()) {
            return false;
        }

        running_ = true;
        asyncThread_ = std::thread([this] { run(); });
        return true;
    }

    void stop() {
        running_ = false;

        if (asyncThread_.joinable()) {
            asyncThread_.join();
        }
    }

    void setCallbacks(const ClientCallbacks& callbacks) {
        callbacks_ = callbacks;
    }

    void onConnected(std::function<void()> callback) {
        callbacks_.onConnected = std::move(callback);
    }

    void onDisconnected(std::function<void(ErrorCode, const std::string&)> callback) {
        callbacks_.onDisconnected = std::move(callback);
    }

    void onError(std::function<void(ErrorCode, const std::string&)> callback) {
        callbacks_.onError = std::move(callback);
    }

    void onReconnecting(std::function<void(uint32_t)> callback) {
        callbacks_.onReconnecting = std::move(callback);
    }

    ClientStats getStats() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return stats_;
    }

    Logger& getLogger() {
        return *logger_;
    }

private:
    void setupTransportCallbacks() {
        transport_->onConnected([this] {
            logger_->info("Client", "Transport connected");
            setState(ConnectionState::Authenticating);
            connectCv_.notify_all();
        });

        transport_->onDisconnected([this](ErrorCode code, const std::string& reason) {
            logger_->info("Client", "Transport disconnected: " + reason);

            bool wasConnected = (state_ == ConnectionState::Connected);

            if (reconnectPolicy_.isEnabled() && reconnectPolicy_.shouldReconnect()) {
                setState(ConnectionState::Reconnecting);
            } else {
                setState(ConnectionState::Disconnected);

                if (callbacks_.onDisconnected) {
                    callbacks_.onDisconnected(code, reason);
                }
            }
        });

        transport_->onError([this](ErrorCode code, const std::string& message) {
            logger_->error("Client", "Transport error: " + message);
            stats_.errorCount++;

            if (callbacks_.onError) {
                callbacks_.onError(code, message);
            }
        });

        transport_->onMessage([this](const std::string& json) {
            handleMessage(json);
        });
    }

    void handleMessage(const std::string& json) {
        auto result = Protocol::deserialize(json);
        if (result.failed()) {
            logger_->error("Client", "Failed to parse message: " + result.errorMessage());
            return;
        }

        const Message& msg = result.value();

        stats_.messagesReceived++;
        stats_.bytesReceived += json.size();
        stats_.lastActivityAt = std::chrono::system_clock::now();

        // Handle based on message type
        switch (msg.type) {
            case MessageType::Auth:
                handleAuthMessage(msg);
                break;

            case MessageType::Message:
                handleSubscriptionMessage(msg);
                break;

            case MessageType::Ack:
                handleAckMessage(msg);
                break;

            case MessageType::Error:
                handleErrorMessage(msg);
                break;

            case MessageType::Pong:
                handlePongMessage(msg);
                break;

            default:
                logger_->debug("Client", "Received message type: " +
                              std::string(messageTypeToString(msg.type)));
                break;
        }
    }

    void handleAuthMessage(const Message& msg) {
        if (authManager_.handleMessage(msg)) {
            if (authManager_.isAuthenticated()) {
                logger_->info("Client", "Authentication successful");
                reconnectPolicy_.reset();
                setState(ConnectionState::Connected);
                stats_.connectedAt = std::chrono::system_clock::now();

                if (callbacks_.onConnected) {
                    callbacks_.onConnected();
                }
            } else {
                logger_->error("Client", "Authentication failed");
                setState(ConnectionState::Disconnected);
            }
        }
        authCv_.notify_all();
    }

    void handleSubscriptionMessage(const Message& msg) {
        std::lock_guard<std::mutex> lock(mutex_);

        // Find matching subscription
        for (auto& [id, sub] : subscriptions_) {
            if (sub.active && matchesSubject(sub.subject, msg.subject)) {
                if (sub.handler) {
                    sub.handler(msg.subject, msg.payload, msg);
                }
            }
        }
    }

    void handleAckMessage(const Message& msg) {
        logger_->debug("Client", "Received ACK for: " + msg.subject);
    }

    void handleErrorMessage(const Message& msg) {
        std::string errorMsg = "Unknown error";

        if (msg.payload.contains("message")) {
            errorMsg = msg.payload["message"].asString();
        }

        logger_->error("Client", "Gateway error: " + errorMsg);

        if (callbacks_.onError) {
            callbacks_.onError(ErrorCode::InternalError, errorMsg);
        }
    }

    void handlePongMessage(const Message&) {
        lastPongReceived_ = std::chrono::steady_clock::now();
        missedPongs_ = 0;
    }

    bool matchesSubject(const std::string& pattern, const std::string& subject) {
        // Simple matching (full pattern matching done in AuthManager)
        if (pattern == subject) return true;
        if (pattern.back() == '>') {
            std::string prefix = pattern.substr(0, pattern.length() - 1);
            return subject.compare(0, prefix.length(), prefix) == 0;
        }
        if (pattern.find('*') != std::string::npos) {
            // Simple wildcard matching - for proper NATS matching use AuthManager
            return true;  // Let the gateway filter
        }
        return false;
    }

    bool doAuthentication() {
        logger_->info("Client", "Starting authentication");

        setState(ConnectionState::Authenticating);

        authManager_.startAuth(config_, [this](const AuthResult& result) {
            // Handled in handleAuthMessage
        });

        // Send auth request
        Message authMsg = AuthManager::createAuthRequest(config_);
        std::string json = Protocol::serialize(authMsg);

        auto result = transport_->send(json);
        if (result.failed()) {
            logger_->error("Client", "Failed to send auth request");
            setState(ConnectionState::Disconnected);
            return false;
        }

        // Wait for auth response
        std::unique_lock<std::mutex> lock(mutex_);
        if (!authCv_.wait_for(lock, config_.authTimeout,
                             [this] { return authManager_.isAuthenticated() ||
                                             state_ == ConnectionState::Disconnected; })) {
            logger_->error("Client", "Authentication timeout");
            transport_->disconnect(1000, "Authentication timeout");
            setState(ConnectionState::Disconnected);
            return false;
        }

        return authManager_.isAuthenticated();
    }

    void processHeartbeat() {
        auto now = std::chrono::steady_clock::now();

        // Check if it's time to send ping
        if (now - lastPingSent_ >= config_.heartbeat.interval) {
            Message ping = Message::ping();
            transport_->send(Protocol::serialize(ping));
            lastPingSent_ = now;
        }

        // Check for missed pongs
        if (lastPongReceived_ != std::chrono::steady_clock::time_point{}) {
            auto sincePong = now - lastPongReceived_;
            if (sincePong > config_.heartbeat.timeout) {
                missedPongs_++;

                if (missedPongs_ >= config_.heartbeat.missedPongsBeforeDisconnect) {
                    logger_->warn("Client", "Heartbeat timeout - connection may be dead");
                    transport_->disconnect(1000, "Heartbeat timeout");
                }
            }
        }
    }

    void processReconnection() {
        if (!reconnectPolicy_.shouldReconnect()) {
            setState(ConnectionState::Disconnected);
            return;
        }

        auto delay = reconnectPolicy_.getNextDelay();
        uint32_t attempt = reconnectPolicy_.getAttemptCount();

        logger_->info("Client", "Reconnecting (attempt " + std::to_string(attempt) +
                     ") in " + std::to_string(delay.count()) + "ms");

        if (callbacks_.onReconnecting) {
            callbacks_.onReconnecting(attempt);
        }

        std::this_thread::sleep_for(delay);

        // Try to reconnect
        auto result = transport_->connect(config_.gatewayUrl, config_.connectTimeout);
        if (result.failed()) {
            logger_->warn("Client", "Reconnection failed: " + result.errorMessage());
            // Will retry on next poll
        }
    }

    void resubscribeAll() {
        if (!reconnectPolicy_.shouldResubscribe()) {
            return;
        }

        std::lock_guard<std::mutex> lock(mutex_);

        for (const auto& [id, sub] : subscriptions_) {
            if (sub.active) {
                Message msg = Message::subscribe(sub.subject);
                transport_->send(Protocol::serialize(msg));
                logger_->info("Client", "Resubscribed to: " + sub.subject);
            }
        }
    }

    void setState(ConnectionState newState) {
        ConnectionState oldState = state_.exchange(newState);

        if (oldState != newState) {
            logger_->debug("Client", std::string("State: ") +
                          connectionStateToString(oldState) + " -> " +
                          connectionStateToString(newState));

            if (callbacks_.onStateChanged) {
                callbacks_.onStateChanged(oldState, newState);
            }

            if (newState == ConnectionState::Connected) {
                resubscribeAll();
            }
        }
    }

    GatewayConfig config_;
    std::shared_ptr<Logger> logger_;
    std::unique_ptr<ITransport> transport_;

    std::atomic<ConnectionState> state_;
    mutable std::mutex mutex_;
    std::condition_variable connectCv_;
    std::condition_variable authCv_;

    AuthManager authManager_;
    ReconnectPolicy reconnectPolicy_;

    std::map<SubscriptionId, Subscription> subscriptions_;
    std::map<std::string, SubscriptionId> subjectToId_;
    std::atomic<SubscriptionId> nextSubscriptionId_;

    ClientCallbacks callbacks_;
    ClientStats stats_;

    std::chrono::steady_clock::time_point lastPingSent_;
    std::chrono::steady_clock::time_point lastPongReceived_;
    uint32_t missedPongs_ = 0;

    std::atomic<bool> running_;
    std::thread asyncThread_;
};

// GatewayClient public implementation
GatewayClient::GatewayClient(const GatewayConfig& config)
    : impl_(std::make_unique<Impl>(config, nullptr))
{}

GatewayClient::GatewayClient(const GatewayConfig& config, std::shared_ptr<Logger> logger)
    : impl_(std::make_unique<Impl>(config, std::move(logger)))
{}

GatewayClient::~GatewayClient() = default;

GatewayClient::GatewayClient(GatewayClient&&) noexcept = default;
GatewayClient& GatewayClient::operator=(GatewayClient&&) noexcept = default;

bool GatewayClient::connect() {
    return impl_->connect();
}

Result<void> GatewayClient::connectAsync() {
    return impl_->connectAsync();
}

void GatewayClient::disconnect() {
    impl_->disconnect();
}

bool GatewayClient::isConnected() const {
    return impl_->isConnected();
}

ConnectionState GatewayClient::getState() const {
    return impl_->getState();
}

const std::optional<DeviceInfo>& GatewayClient::getDeviceInfo() const {
    return impl_->getDeviceInfo();
}

Result<void> GatewayClient::publish(const std::string& subject, const JsonValue& payload) {
    return impl_->publish(subject, payload);
}

Result<void> GatewayClient::publish(const std::string& subject, const std::string& payload) {
    return impl_->publish(subject, payload);
}

Result<void> GatewayClient::publish(const std::string& subject, const JsonValue& payload, QoS qos) {
    return impl_->publish(subject, payload, qos);
}

Result<SubscriptionId> GatewayClient::subscribe(const std::string& subject, SubscriptionHandler handler) {
    return impl_->subscribe(subject, std::move(handler));
}

Result<SubscriptionId> GatewayClient::subscribe(const std::string& subject, MessageHandler handler) {
    return impl_->subscribe(subject, std::move(handler));
}

Result<void> GatewayClient::unsubscribe(SubscriptionId subscriptionId) {
    return impl_->unsubscribe(subscriptionId);
}

Result<void> GatewayClient::unsubscribe(const std::string& subject) {
    return impl_->unsubscribe(subject);
}

std::vector<std::string> GatewayClient::getSubscriptions() const {
    return impl_->getSubscriptions();
}

void GatewayClient::poll(Duration timeout) {
    impl_->poll(timeout);
}

void GatewayClient::run() {
    impl_->run();
}

bool GatewayClient::runAsync() {
    return impl_->runAsync();
}

void GatewayClient::stop() {
    impl_->stop();
}

void GatewayClient::setCallbacks(const ClientCallbacks& callbacks) {
    impl_->setCallbacks(callbacks);
}

void GatewayClient::onConnected(std::function<void()> callback) {
    impl_->onConnected(std::move(callback));
}

void GatewayClient::onDisconnected(std::function<void(ErrorCode, const std::string&)> callback) {
    impl_->onDisconnected(std::move(callback));
}

void GatewayClient::onError(std::function<void(ErrorCode, const std::string&)> callback) {
    impl_->onError(std::move(callback));
}

void GatewayClient::onReconnecting(std::function<void(uint32_t)> callback) {
    impl_->onReconnecting(std::move(callback));
}

ClientStats GatewayClient::getStats() const {
    return impl_->getStats();
}

Logger& GatewayClient::getLogger() {
    return impl_->getLogger();
}

const char* GatewayClient::getVersion() {
    return Version::STRING;
}

const char* GatewayClient::getProtocolVersion() {
    return Version::PROTOCOL;
}

} // namespace gateway
