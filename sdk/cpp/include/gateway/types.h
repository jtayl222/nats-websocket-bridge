/**
 * @file types.h
 * @brief Core type definitions for the Gateway Device SDK
 *
 * This file defines the fundamental types used throughout the SDK,
 * matching the protocol defined by the NATS WebSocket Bridge Gateway.
 */

#pragma once

#include <string>
#include <cstdint>
#include <chrono>
#include <vector>

namespace gateway {

/**
 * @brief SDK version information
 */
struct Version {
    static constexpr int MAJOR = 1;
    static constexpr int MINOR = 0;
    static constexpr int PATCH = 0;
    static constexpr const char* STRING = "1.0.0";
    static constexpr const char* PROTOCOL = "1.0";
};

/**
 * @brief Message types matching the gateway protocol
 *
 * These values must match the C# MessageType enum in the gateway:
 * - Publish = 0
 * - Subscribe = 1
 * - Unsubscribe = 2
 * - Message = 3
 * - Request = 4
 * - Reply = 5
 * - Ack = 6
 * - Error = 7
 * - Auth = 8
 * - Ping = 9
 * - Pong = 10
 */
enum class MessageType : int {
    Publish = 0,
    Subscribe = 1,
    Unsubscribe = 2,
    Message = 3,
    Request = 4,
    Reply = 5,
    Ack = 6,
    Error = 7,
    Auth = 8,
    Ping = 9,
    Pong = 10
};

/**
 * @brief Convert MessageType to string for debugging
 */
inline const char* messageTypeToString(MessageType type) {
    switch (type) {
        case MessageType::Publish: return "Publish";
        case MessageType::Subscribe: return "Subscribe";
        case MessageType::Unsubscribe: return "Unsubscribe";
        case MessageType::Message: return "Message";
        case MessageType::Request: return "Request";
        case MessageType::Reply: return "Reply";
        case MessageType::Ack: return "Ack";
        case MessageType::Error: return "Error";
        case MessageType::Auth: return "Auth";
        case MessageType::Ping: return "Ping";
        case MessageType::Pong: return "Pong";
        default: return "Unknown";
    }
}

/**
 * @brief Connection state of the client
 */
enum class ConnectionState {
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Closing,
    Closed
};

/**
 * @brief Convert ConnectionState to string for debugging
 */
inline const char* connectionStateToString(ConnectionState state) {
    switch (state) {
        case ConnectionState::Disconnected: return "Disconnected";
        case ConnectionState::Connecting: return "Connecting";
        case ConnectionState::Authenticating: return "Authenticating";
        case ConnectionState::Connected: return "Connected";
        case ConnectionState::Reconnecting: return "Reconnecting";
        case ConnectionState::Closing: return "Closing";
        case ConnectionState::Closed: return "Closed";
        default: return "Unknown";
    }
}

/**
 * @brief Quality of Service levels for message delivery
 */
enum class QoS {
    AtMostOnce = 0,    // Fire and forget
    AtLeastOnce = 1,   // Guaranteed delivery (via JetStream)
    ExactlyOnce = 2    // Reserved for future use
};

/**
 * @brief Device types recognized by the gateway
 */
enum class DeviceType {
    Sensor,
    Actuator,
    Controller,
    Gateway,
    Custom
};

/**
 * @brief Convert DeviceType to string
 */
inline const char* deviceTypeToString(DeviceType type) {
    switch (type) {
        case DeviceType::Sensor: return "sensor";
        case DeviceType::Actuator: return "actuator";
        case DeviceType::Controller: return "controller";
        case DeviceType::Gateway: return "gateway";
        case DeviceType::Custom: return "custom";
        default: return "unknown";
    }
}

/**
 * @brief Parse string to DeviceType
 */
inline DeviceType deviceTypeFromString(const std::string& str) {
    if (str == "sensor") return DeviceType::Sensor;
    if (str == "actuator") return DeviceType::Actuator;
    if (str == "controller") return DeviceType::Controller;
    if (str == "gateway") return DeviceType::Gateway;
    return DeviceType::Custom;
}

/**
 * @brief Device information returned after authentication
 */
struct DeviceInfo {
    std::string deviceId;
    std::string deviceType;
    bool isConnected = false;
    std::chrono::system_clock::time_point connectedAt;
    std::chrono::system_clock::time_point lastActivityAt;
    std::vector<std::string> allowedPublishTopics;
    std::vector<std::string> allowedSubscribeTopics;
};

/**
 * @brief Subscription handle for managing subscriptions
 */
using SubscriptionId = uint64_t;

/**
 * @brief Timestamp type used throughout the SDK
 */
using Timestamp = std::chrono::system_clock::time_point;

/**
 * @brief Duration type for timeouts and intervals
 */
using Duration = std::chrono::milliseconds;

} // namespace gateway
