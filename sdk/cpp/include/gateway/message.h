/**
 * @file message.h
 * @brief Message types for the Gateway Device SDK
 *
 * This file defines the message structures that match the gateway protocol.
 */

#pragma once

#include "types.h"
#include <string>
#include <optional>
#include <chrono>
#include <any>
#include <variant>
#include <vector>
#include <map>

namespace gateway {

/**
 * @brief JSON value type for dynamic payloads
 *
 * Can hold: null, bool, int64, double, string, array, object
 */
class JsonValue {
public:
    using Array = std::vector<JsonValue>;
    using Object = std::map<std::string, JsonValue>;

    JsonValue() : type_(Type::Null) {}
    JsonValue(std::nullptr_t) : type_(Type::Null) {}
    JsonValue(bool v) : type_(Type::Bool), boolValue_(v) {}
    JsonValue(int v) : type_(Type::Int), intValue_(v) {}
    JsonValue(int64_t v) : type_(Type::Int), intValue_(v) {}
    JsonValue(double v) : type_(Type::Double), doubleValue_(v) {}
    JsonValue(const char* v) : type_(Type::String), stringValue_(v) {}
    JsonValue(const std::string& v) : type_(Type::String), stringValue_(v) {}
    JsonValue(std::string&& v) : type_(Type::String), stringValue_(std::move(v)) {}
    JsonValue(const Array& v) : type_(Type::Array), arrayValue_(v) {}
    JsonValue(Array&& v) : type_(Type::Array), arrayValue_(std::move(v)) {}
    JsonValue(const Object& v) : type_(Type::Object), objectValue_(v) {}
    JsonValue(Object&& v) : type_(Type::Object), objectValue_(std::move(v)) {}

    enum class Type { Null, Bool, Int, Double, String, Array, Object };

    Type type() const { return type_; }

    bool isNull() const { return type_ == Type::Null; }
    bool isBool() const { return type_ == Type::Bool; }
    bool isInt() const { return type_ == Type::Int; }
    bool isDouble() const { return type_ == Type::Double; }
    bool isNumber() const { return type_ == Type::Int || type_ == Type::Double; }
    bool isString() const { return type_ == Type::String; }
    bool isArray() const { return type_ == Type::Array; }
    bool isObject() const { return type_ == Type::Object; }

    bool asBool() const { return boolValue_; }
    int64_t asInt() const { return intValue_; }
    double asDouble() const {
        return type_ == Type::Int ? static_cast<double>(intValue_) : doubleValue_;
    }
    const std::string& asString() const { return stringValue_; }
    const Array& asArray() const { return arrayValue_; }
    const Object& asObject() const { return objectValue_; }

    Array& asArray() { return arrayValue_; }
    Object& asObject() { return objectValue_; }

    // Object access
    JsonValue& operator[](const std::string& key) {
        if (type_ != Type::Object) {
            type_ = Type::Object;
            objectValue_.clear();
        }
        return objectValue_[key];
    }

    const JsonValue& operator[](const std::string& key) const {
        static JsonValue null;
        auto it = objectValue_.find(key);
        return it != objectValue_.end() ? it->second : null;
    }

    bool contains(const std::string& key) const {
        return type_ == Type::Object && objectValue_.find(key) != objectValue_.end();
    }

    // Array access
    JsonValue& operator[](size_t index) {
        return arrayValue_[index];
    }

    const JsonValue& operator[](size_t index) const {
        return arrayValue_[index];
    }

    size_t size() const {
        if (type_ == Type::Array) return arrayValue_.size();
        if (type_ == Type::Object) return objectValue_.size();
        return 0;
    }

    // Builder methods for object construction
    static JsonValue object() {
        return JsonValue(Object{});
    }

    static JsonValue array() {
        return JsonValue(Array{});
    }

private:
    Type type_;
    bool boolValue_ = false;
    int64_t intValue_ = 0;
    double doubleValue_ = 0.0;
    std::string stringValue_;
    Array arrayValue_;
    Object objectValue_;
};

/**
 * @brief Gateway message matching the C# GatewayMessage model
 *
 * JSON structure:
 * {
 *   "type": <int>,           // MessageType enum value
 *   "subject": "<string>",   // NATS subject/topic
 *   "payload": <any>,        // JSON payload
 *   "correlationId": "<string>",  // For request/reply
 *   "timestamp": "<ISO8601>",     // UTC timestamp
 *   "deviceId": "<string>"        // Set by gateway
 * }
 */
struct Message {
    /// Message type
    MessageType type = MessageType::Publish;

    /// NATS subject/topic
    std::string subject;

    /// Message payload (any JSON value)
    JsonValue payload;

    /// Correlation ID for request/reply patterns
    std::optional<std::string> correlationId;

    /// Timestamp (set automatically if not provided)
    std::optional<Timestamp> timestamp;

    /// Device ID (set by gateway, ignored when sending)
    std::optional<std::string> deviceId;

    /// Default constructor
    Message() = default;

    /// Construct a publish message
    static Message publish(const std::string& subject, const JsonValue& payload) {
        Message msg;
        msg.type = MessageType::Publish;
        msg.subject = subject;
        msg.payload = payload;
        return msg;
    }

    /// Construct a subscribe message
    static Message subscribe(const std::string& subject) {
        Message msg;
        msg.type = MessageType::Subscribe;
        msg.subject = subject;
        return msg;
    }

    /// Construct an unsubscribe message
    static Message unsubscribe(const std::string& subject) {
        Message msg;
        msg.type = MessageType::Unsubscribe;
        msg.subject = subject;
        return msg;
    }

    /// Construct a ping message
    static Message ping() {
        Message msg;
        msg.type = MessageType::Ping;
        return msg;
    }

    /// Construct a pong message
    static Message pong() {
        Message msg;
        msg.type = MessageType::Pong;
        return msg;
    }
};

/**
 * @brief Authentication request payload
 *
 * JSON structure:
 * {
 *   "deviceId": "<string>",
 *   "token": "<string>",
 *   "deviceType": "<string>"
 * }
 */
struct AuthRequest {
    std::string deviceId;
    std::string token;
    std::string deviceType;
};

/**
 * @brief Authentication response payload
 *
 * JSON structure:
 * {
 *   "success": <bool>,
 *   "device": { ... },
 *   "message": "<string>"
 * }
 */
struct AuthResponse {
    bool success = false;
    std::optional<DeviceInfo> device;
    std::string message;
};

/**
 * @brief Error message payload from gateway
 */
struct ErrorPayload {
    std::string message;
    std::string code;
    std::optional<std::string> details;
};

/**
 * @brief Subscription acknowledgment payload
 */
struct SubscriptionAck {
    std::string subject;
    bool success = false;
    std::string message;
};

/**
 * @brief Callback type for received messages
 */
using MessageHandler = std::function<void(const Message& message)>;

/**
 * @brief Callback type for subscription-specific messages
 */
using SubscriptionHandler = std::function<void(
    const std::string& subject,
    const JsonValue& payload,
    const Message& fullMessage
)>;

/**
 * @brief Subscription information
 */
struct Subscription {
    SubscriptionId id;
    std::string subject;
    SubscriptionHandler handler;
    bool active = true;
};

} // namespace gateway
