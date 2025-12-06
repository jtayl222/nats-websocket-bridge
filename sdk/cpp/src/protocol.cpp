/**
 * @file protocol.cpp
 * @brief Protocol serialization implementation using nlohmann/json
 */

#include "gateway/protocol.h"
#include <nlohmann/json.hpp>
#include <sstream>
#include <iomanip>
#include <regex>
#include <chrono>
#include <ctime>

namespace gateway {

using json = nlohmann::json;

// Helper to convert JsonValue to nlohmann::json
static json jsonValueToNlohmann(const JsonValue& value) {
    switch (value.type()) {
        case JsonValue::Type::Null:
            return nullptr;
        case JsonValue::Type::Bool:
            return value.asBool();
        case JsonValue::Type::Int:
            return value.asInt();
        case JsonValue::Type::Double:
            return value.asDouble();
        case JsonValue::Type::String:
            return value.asString();
        case JsonValue::Type::Array: {
            json arr = json::array();
            for (const auto& item : value.asArray()) {
                arr.push_back(jsonValueToNlohmann(item));
            }
            return arr;
        }
        case JsonValue::Type::Object: {
            json obj = json::object();
            for (const auto& [key, val] : value.asObject()) {
                obj[key] = jsonValueToNlohmann(val);
            }
            return obj;
        }
    }
    return nullptr;
}

// Helper to convert nlohmann::json to JsonValue
static JsonValue nlohmannToJsonValue(const json& j) {
    if (j.is_null()) {
        return JsonValue();
    }
    if (j.is_boolean()) {
        return JsonValue(j.get<bool>());
    }
    if (j.is_number_integer()) {
        return JsonValue(j.get<int64_t>());
    }
    if (j.is_number_float()) {
        return JsonValue(j.get<double>());
    }
    if (j.is_string()) {
        return JsonValue(j.get<std::string>());
    }
    if (j.is_array()) {
        JsonValue::Array arr;
        for (const auto& item : j) {
            arr.push_back(nlohmannToJsonValue(item));
        }
        return JsonValue(std::move(arr));
    }
    if (j.is_object()) {
        JsonValue::Object obj;
        for (auto it = j.begin(); it != j.end(); ++it) {
            obj[it.key()] = nlohmannToJsonValue(it.value());
        }
        return JsonValue(std::move(obj));
    }
    return JsonValue();
}

std::string Protocol::serialize(const Message& message) {
    json j;

    j["type"] = static_cast<int>(message.type);

    if (!message.subject.empty()) {
        j["subject"] = message.subject;
    }

    if (!message.payload.isNull()) {
        j["payload"] = jsonValueToNlohmann(message.payload);
    }

    if (message.correlationId.has_value()) {
        j["correlationId"] = message.correlationId.value();
    }

    if (message.timestamp.has_value()) {
        // Format as ISO 8601
        auto time = std::chrono::system_clock::to_time_t(message.timestamp.value());
        std::ostringstream oss;
        oss << std::put_time(std::gmtime(&time), "%Y-%m-%dT%H:%M:%S") << "Z";
        j["timestamp"] = oss.str();
    } else {
        j["timestamp"] = getTimestamp();
    }

    if (message.deviceId.has_value()) {
        j["deviceId"] = message.deviceId.value();
    }

    return j.dump();
}

Result<Message> Protocol::deserialize(const std::string& jsonStr) {
    try {
        json j = json::parse(jsonStr);

        Message msg;

        if (j.contains("type")) {
            msg.type = static_cast<MessageType>(j["type"].get<int>());
        }

        if (j.contains("subject")) {
            msg.subject = j["subject"].get<std::string>();
        }

        if (j.contains("payload")) {
            msg.payload = nlohmannToJsonValue(j["payload"]);
        }

        if (j.contains("correlationId")) {
            msg.correlationId = j["correlationId"].get<std::string>();
        }

        if (j.contains("timestamp")) {
            auto result = parseTimestamp(j["timestamp"].get<std::string>());
            if (result.ok()) {
                msg.timestamp = result.value();
            }
        }

        if (j.contains("deviceId")) {
            msg.deviceId = j["deviceId"].get<std::string>();
        }

        return Result<Message>(std::move(msg));
    } catch (const json::exception& e) {
        return Result<Message>(ErrorCode::MalformedJson,
                               std::string("JSON parse error: ") + e.what());
    }
}

std::string Protocol::serializeAuthRequest(const AuthRequest& request) {
    Message msg;
    msg.type = MessageType::Auth;

    JsonValue payload = JsonValue::object();
    payload["deviceId"] = request.deviceId;
    payload["token"] = request.token;
    payload["deviceType"] = request.deviceType;

    msg.payload = std::move(payload);

    return serialize(msg);
}

Result<AuthResponse> Protocol::deserializeAuthResponse(const std::string& jsonStr) {
    try {
        auto msgResult = deserialize(jsonStr);
        if (msgResult.failed()) {
            return Result<AuthResponse>(msgResult.error(), msgResult.errorMessage());
        }

        const Message& msg = msgResult.value();

        if (msg.type != MessageType::Auth) {
            return Result<AuthResponse>(ErrorCode::InvalidMessageType,
                                        "Expected Auth message type");
        }

        AuthResponse response;

        const auto& payload = msg.payload;

        if (payload.contains("success")) {
            response.success = payload["success"].asBool();
        }

        if (payload.contains("message")) {
            response.message = payload["message"].asString();
        }

        if (payload.contains("device") && payload["device"].isObject()) {
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

            response.device = std::move(device);
        }

        return Result<AuthResponse>(std::move(response));
    } catch (const std::exception& e) {
        return Result<AuthResponse>(ErrorCode::MalformedJson,
                                    std::string("Parse error: ") + e.what());
    }
}

std::string Protocol::serializeJsonValue(const JsonValue& value) {
    json j = jsonValueToNlohmann(value);
    return j.dump();
}

Result<JsonValue> Protocol::deserializeJsonValue(const std::string& jsonStr) {
    try {
        json j = json::parse(jsonStr);
        return Result<JsonValue>(nlohmannToJsonValue(j));
    } catch (const json::exception& e) {
        return Result<JsonValue>(ErrorCode::MalformedJson,
                                 std::string("JSON parse error: ") + e.what());
    }
}

bool Protocol::isValidSubject(const std::string& subject) {
    if (subject.empty()) {
        return false;
    }

    if (subject.length() > 256) {
        return false;
    }

    // Cannot start or end with dot
    if (subject.front() == '.' || subject.back() == '.') {
        return false;
    }

    // Cannot contain consecutive dots
    if (subject.find("..") != std::string::npos) {
        return false;
    }

    // Only allowed characters: alphanumeric, ., *, >, -, _
    static const std::regex validPattern("^[a-zA-Z0-9.*>_-]+$");
    if (!std::regex_match(subject, validPattern)) {
        return false;
    }

    // > can only appear at the end
    size_t gtPos = subject.find('>');
    if (gtPos != std::string::npos) {
        if (gtPos != subject.length() - 1) {
            return false;
        }
        // > must be preceded by . or be alone
        if (gtPos > 0 && subject[gtPos - 1] != '.') {
            return false;
        }
    }

    return true;
}

std::string Protocol::getTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto time = std::chrono::system_clock::to_time_t(now);
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;

    std::ostringstream oss;
    oss << std::put_time(std::gmtime(&time), "%Y-%m-%dT%H:%M:%S");
    oss << '.' << std::setfill('0') << std::setw(3) << ms.count() << 'Z';

    return oss.str();
}

Result<Timestamp> Protocol::parseTimestamp(const std::string& timestamp) {
    std::tm tm = {};
    std::istringstream ss(timestamp);

    // Try parsing ISO 8601 format
    ss >> std::get_time(&tm, "%Y-%m-%dT%H:%M:%S");

    if (ss.fail()) {
        return Result<Timestamp>(ErrorCode::MalformedJson, "Invalid timestamp format");
    }

    // Convert to time_point
    auto time = std::mktime(&tm);
    return Result<Timestamp>(std::chrono::system_clock::from_time_t(time));
}

} // namespace gateway
