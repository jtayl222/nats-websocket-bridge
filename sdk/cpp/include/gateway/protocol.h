/**
 * @file protocol.h
 * @brief Protocol serialization/deserialization for Gateway messages
 */

#pragma once

#include "types.h"
#include "message.h"
#include "error.h"
#include <string>

namespace gateway {

/**
 * @brief Protocol serializer/deserializer
 *
 * Handles conversion between Message objects and JSON strings
 * matching the gateway protocol format.
 */
class Protocol {
public:
    /**
     * @brief Serialize a Message to JSON string
     * @param message Message to serialize
     * @return JSON string
     */
    static std::string serialize(const Message& message);

    /**
     * @brief Deserialize JSON string to Message
     * @param json JSON string
     * @return Result containing Message or error
     */
    static Result<Message> deserialize(const std::string& json);

    /**
     * @brief Serialize an authentication request
     * @param request Auth request
     * @return JSON string for auth message
     */
    static std::string serializeAuthRequest(const AuthRequest& request);

    /**
     * @brief Deserialize an authentication response
     * @param json JSON string from auth message payload
     * @return Result containing AuthResponse or error
     */
    static Result<AuthResponse> deserializeAuthResponse(const std::string& json);

    /**
     * @brief Serialize JsonValue to string
     * @param value JsonValue to serialize
     * @return JSON string
     */
    static std::string serializeJsonValue(const JsonValue& value);

    /**
     * @brief Deserialize string to JsonValue
     * @param json JSON string
     * @return Result containing JsonValue or error
     */
    static Result<JsonValue> deserializeJsonValue(const std::string& json);

    /**
     * @brief Validate a subject string
     * @param subject Subject to validate
     * @return true if valid
     */
    static bool isValidSubject(const std::string& subject);

    /**
     * @brief Get current timestamp in ISO 8601 format
     * @return Timestamp string
     */
    static std::string getTimestamp();

    /**
     * @brief Parse ISO 8601 timestamp
     * @param timestamp Timestamp string
     * @return Result containing Timestamp or error
     */
    static Result<Timestamp> parseTimestamp(const std::string& timestamp);
};

} // namespace gateway
