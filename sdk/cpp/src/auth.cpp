/**
 * @file auth.cpp
 * @brief Authentication manager implementation
 */

#include "gateway/auth.h"
#include "gateway/protocol.h"
#include <algorithm>
#include <sstream>

namespace gateway {

Message AuthManager::createAuthRequest(const GatewayConfig& config) {
    Message msg;
    msg.type = MessageType::Auth;

    JsonValue payload = JsonValue::object();
    payload["deviceId"] = config.deviceId;
    payload["token"] = config.authToken;
    payload["deviceType"] = config.getDeviceTypeString();

    msg.payload = std::move(payload);

    return msg;
}

AuthResult AuthManager::processAuthResponse(const Message& message) {
    AuthResult result;

    if (message.type != MessageType::Auth) {
        result.success = false;
        result.error = ErrorCode::InvalidMessageType;
        result.message = "Expected Auth message type";
        return result;
    }

    const auto& payload = message.payload;

    if (payload.contains("success")) {
        result.success = payload["success"].asBool();
    }

    if (payload.contains("message")) {
        result.message = payload["message"].asString();
    }

    if (result.success && payload.contains("device") && payload["device"].isObject()) {
        DeviceInfo device;
        const auto& deviceObj = payload["device"];

        if (deviceObj.contains("deviceId")) {
            device.deviceId = deviceObj["deviceId"].asString();
        }
        if (deviceObj.contains("deviceType")) {
            device.deviceType = deviceObj["deviceType"].asString();
        }
        if (deviceObj.contains("isConnected")) {
            device.isConnected = deviceObj["isConnected"].asBool();
        }

        if (deviceObj.contains("allowedPublishTopics") &&
            deviceObj["allowedPublishTopics"].isArray()) {
            for (const auto& topic : deviceObj["allowedPublishTopics"].asArray()) {
                if (topic.isString()) {
                    device.allowedPublishTopics.push_back(topic.asString());
                }
            }
        }

        if (deviceObj.contains("allowedSubscribeTopics") &&
            deviceObj["allowedSubscribeTopics"].isArray()) {
            for (const auto& topic : deviceObj["allowedSubscribeTopics"].asArray()) {
                if (topic.isString()) {
                    device.allowedSubscribeTopics.push_back(topic.asString());
                }
            }
        }

        result.deviceInfo = std::move(device);
    }

    if (!result.success) {
        result.error = ErrorCode::AuthenticationFailed;
    }

    return result;
}

void AuthManager::startAuth(const GatewayConfig& config, AuthCompleteCallback callback) {
    state_ = AuthState::Authenticating;
    callback_ = std::move(callback);
}

bool AuthManager::handleMessage(const Message& message) {
    if (state_ != AuthState::Authenticating) {
        return false;
    }

    if (message.type != MessageType::Auth) {
        return false;
    }

    AuthResult result = processAuthResponse(message);

    if (result.success) {
        state_ = AuthState::Authenticated;
        deviceInfo_ = result.deviceInfo;
    } else {
        state_ = AuthState::Failed;
    }

    if (callback_) {
        callback_(result);
        callback_ = nullptr;
    }

    return true;
}

void AuthManager::reset() {
    state_ = AuthState::NotAuthenticated;
    deviceInfo_.reset();
    callback_ = nullptr;
}

bool AuthManager::canPublish(const std::string& subject) const {
    if (!deviceInfo_.has_value()) {
        return false;
    }

    const auto& topics = deviceInfo_->allowedPublishTopics;

    // Empty list means no restrictions (or all allowed - depends on policy)
    // For safety, we treat empty as "deny all"
    if (topics.empty()) {
        return false;
    }

    for (const auto& pattern : topics) {
        if (matchesPattern(pattern, subject)) {
            return true;
        }
    }

    return false;
}

bool AuthManager::canSubscribe(const std::string& subject) const {
    if (!deviceInfo_.has_value()) {
        return false;
    }

    const auto& topics = deviceInfo_->allowedSubscribeTopics;

    if (topics.empty()) {
        return false;
    }

    for (const auto& pattern : topics) {
        if (matchesPattern(pattern, subject)) {
            return true;
        }
    }

    return false;
}

bool AuthManager::matchesPattern(const std::string& pattern, const std::string& subject) {
    // Exact match
    if (pattern == subject) {
        return true;
    }

    // Split into tokens
    auto splitTokens = [](const std::string& s) -> std::vector<std::string> {
        std::vector<std::string> tokens;
        std::istringstream iss(s);
        std::string token;
        while (std::getline(iss, token, '.')) {
            tokens.push_back(token);
        }
        return tokens;
    };

    auto patternTokens = splitTokens(pattern);
    auto subjectTokens = splitTokens(subject);

    size_t pi = 0;
    size_t si = 0;

    while (pi < patternTokens.size() && si < subjectTokens.size()) {
        const std::string& pt = patternTokens[pi];

        if (pt == ">") {
            // > matches everything remaining
            return true;
        } else if (pt == "*") {
            // * matches exactly one token
            pi++;
            si++;
        } else if (pt == subjectTokens[si]) {
            // Exact token match
            pi++;
            si++;
        } else {
            return false;
        }
    }

    // Both must be exhausted for a match (unless pattern ended with >)
    return pi == patternTokens.size() && si == subjectTokens.size();
}

} // namespace gateway
