/**
 * @file auth.h
 * @brief Authentication handling for the Gateway Device SDK
 */

#pragma once

#include "types.h"
#include "message.h"
#include "error.h"
#include "config.h"
#include <string>
#include <functional>
#include <future>

namespace gateway {

/**
 * @brief Authentication state
 */
enum class AuthState {
    NotAuthenticated,
    Authenticating,
    Authenticated,
    Failed
};

/**
 * @brief Convert AuthState to string
 */
inline const char* authStateToString(AuthState state) {
    switch (state) {
        case AuthState::NotAuthenticated: return "NotAuthenticated";
        case AuthState::Authenticating: return "Authenticating";
        case AuthState::Authenticated: return "Authenticated";
        case AuthState::Failed: return "Failed";
        default: return "Unknown";
    }
}

/**
 * @brief Authentication result
 */
struct AuthResult {
    bool success = false;
    ErrorCode error = ErrorCode::Success;
    std::string message;
    std::optional<DeviceInfo> deviceInfo;
};

/**
 * @brief Authentication manager
 *
 * Handles the authentication handshake with the gateway.
 */
class AuthManager {
public:
    using AuthCompleteCallback = std::function<void(const AuthResult& result)>;

    /**
     * @brief Create an authentication request message
     * @param config Gateway configuration
     * @return Auth request message
     */
    static Message createAuthRequest(const GatewayConfig& config);

    /**
     * @brief Process an authentication response
     * @param message Response message from gateway
     * @return Authentication result
     */
    static AuthResult processAuthResponse(const Message& message);

    /**
     * @brief Get current auth state
     */
    AuthState getState() const { return state_; }

    /**
     * @brief Get device info after successful auth
     */
    const std::optional<DeviceInfo>& getDeviceInfo() const { return deviceInfo_; }

    /**
     * @brief Check if authenticated
     */
    bool isAuthenticated() const { return state_ == AuthState::Authenticated; }

    /**
     * @brief Start authentication
     * @param config Configuration
     * @param callback Completion callback
     */
    void startAuth(const GatewayConfig& config, AuthCompleteCallback callback);

    /**
     * @brief Handle incoming message during auth
     * @param message Message to handle
     * @return true if message was handled
     */
    bool handleMessage(const Message& message);

    /**
     * @brief Reset authentication state
     */
    void reset();

    /**
     * @brief Check if a subject is allowed for publishing
     * @param subject Subject to check
     * @return true if allowed
     */
    bool canPublish(const std::string& subject) const;

    /**
     * @brief Check if a subject is allowed for subscribing
     * @param subject Subject to check
     * @return true if allowed
     */
    bool canSubscribe(const std::string& subject) const;

private:
    /**
     * @brief Match subject against pattern (with NATS wildcards)
     */
    static bool matchesPattern(const std::string& pattern, const std::string& subject);

    AuthState state_ = AuthState::NotAuthenticated;
    std::optional<DeviceInfo> deviceInfo_;
    AuthCompleteCallback callback_;
};

} // namespace gateway
