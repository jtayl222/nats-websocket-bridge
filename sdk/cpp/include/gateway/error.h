/**
 * @file error.h
 * @brief Error handling for the Gateway Device SDK
 */

#pragma once

#include <string>
#include <stdexcept>
#include <system_error>

namespace gateway {

/**
 * @brief Error codes for SDK operations
 */
enum class ErrorCode {
    Success = 0,

    // Connection errors (100-199)
    ConnectionFailed = 100,
    ConnectionTimeout = 101,
    ConnectionClosed = 102,
    ConnectionLost = 103,
    TlsError = 104,
    DnsResolutionFailed = 105,

    // Authentication errors (200-299)
    AuthenticationFailed = 200,
    AuthenticationTimeout = 201,
    InvalidCredentials = 202,
    DeviceNotRegistered = 203,
    TokenExpired = 204,

    // Authorization errors (300-399)
    NotAuthorized = 300,
    PublishNotAllowed = 301,
    SubscribeNotAllowed = 302,
    TopicNotAllowed = 303,

    // Protocol errors (400-499)
    InvalidMessage = 400,
    InvalidMessageType = 401,
    InvalidSubject = 402,
    PayloadTooLarge = 403,
    MalformedJson = 404,
    ProtocolVersionMismatch = 405,

    // Operation errors (500-599)
    OperationTimeout = 500,
    OperationCancelled = 501,
    AlreadyConnected = 502,
    NotConnected = 503,
    AlreadySubscribed = 504,
    NotSubscribed = 505,
    RateLimitExceeded = 506,
    BufferFull = 507,

    // Internal errors (900-999)
    InternalError = 900,
    MemoryAllocationFailed = 901,
    ThreadError = 902,
    Unknown = 999
};

/**
 * @brief Convert ErrorCode to string
 */
inline const char* errorCodeToString(ErrorCode code) {
    switch (code) {
        case ErrorCode::Success: return "Success";
        case ErrorCode::ConnectionFailed: return "ConnectionFailed";
        case ErrorCode::ConnectionTimeout: return "ConnectionTimeout";
        case ErrorCode::ConnectionClosed: return "ConnectionClosed";
        case ErrorCode::ConnectionLost: return "ConnectionLost";
        case ErrorCode::TlsError: return "TlsError";
        case ErrorCode::DnsResolutionFailed: return "DnsResolutionFailed";
        case ErrorCode::AuthenticationFailed: return "AuthenticationFailed";
        case ErrorCode::AuthenticationTimeout: return "AuthenticationTimeout";
        case ErrorCode::InvalidCredentials: return "InvalidCredentials";
        case ErrorCode::DeviceNotRegistered: return "DeviceNotRegistered";
        case ErrorCode::TokenExpired: return "TokenExpired";
        case ErrorCode::NotAuthorized: return "NotAuthorized";
        case ErrorCode::PublishNotAllowed: return "PublishNotAllowed";
        case ErrorCode::SubscribeNotAllowed: return "SubscribeNotAllowed";
        case ErrorCode::TopicNotAllowed: return "TopicNotAllowed";
        case ErrorCode::InvalidMessage: return "InvalidMessage";
        case ErrorCode::InvalidMessageType: return "InvalidMessageType";
        case ErrorCode::InvalidSubject: return "InvalidSubject";
        case ErrorCode::PayloadTooLarge: return "PayloadTooLarge";
        case ErrorCode::MalformedJson: return "MalformedJson";
        case ErrorCode::ProtocolVersionMismatch: return "ProtocolVersionMismatch";
        case ErrorCode::OperationTimeout: return "OperationTimeout";
        case ErrorCode::OperationCancelled: return "OperationCancelled";
        case ErrorCode::AlreadyConnected: return "AlreadyConnected";
        case ErrorCode::NotConnected: return "NotConnected";
        case ErrorCode::AlreadySubscribed: return "AlreadySubscribed";
        case ErrorCode::NotSubscribed: return "NotSubscribed";
        case ErrorCode::RateLimitExceeded: return "RateLimitExceeded";
        case ErrorCode::BufferFull: return "BufferFull";
        case ErrorCode::InternalError: return "InternalError";
        case ErrorCode::MemoryAllocationFailed: return "MemoryAllocationFailed";
        case ErrorCode::ThreadError: return "ThreadError";
        case ErrorCode::Unknown: return "Unknown";
        default: return "Unknown";
    }
}

/**
 * @brief Error category for std::error_code integration
 */
class GatewayErrorCategory : public std::error_category {
public:
    const char* name() const noexcept override {
        return "gateway";
    }

    std::string message(int ev) const override {
        return errorCodeToString(static_cast<ErrorCode>(ev));
    }

    static const GatewayErrorCategory& instance() {
        static GatewayErrorCategory instance;
        return instance;
    }
};

/**
 * @brief Create std::error_code from ErrorCode
 */
inline std::error_code make_error_code(ErrorCode e) {
    return {static_cast<int>(e), GatewayErrorCategory::instance()};
}

/**
 * @brief Result type for operations that can fail
 */
template<typename T>
class Result {
public:
    Result(T value) : value_(std::move(value)), error_(ErrorCode::Success) {}
    Result(ErrorCode error, std::string message = "")
        : error_(error), errorMessage_(std::move(message)) {}

    bool ok() const { return error_ == ErrorCode::Success; }
    bool failed() const { return !ok(); }

    const T& value() const {
        if (failed()) {
            throw std::runtime_error("Attempted to access value of failed Result");
        }
        return value_;
    }

    T& value() {
        if (failed()) {
            throw std::runtime_error("Attempted to access value of failed Result");
        }
        return value_;
    }

    ErrorCode error() const { return error_; }
    const std::string& errorMessage() const { return errorMessage_; }

    explicit operator bool() const { return ok(); }

private:
    T value_{};
    ErrorCode error_;
    std::string errorMessage_;
};

/**
 * @brief Specialization for void results
 */
template<>
class Result<void> {
public:
    Result() : error_(ErrorCode::Success) {}
    Result(ErrorCode error, std::string message = "")
        : error_(error), errorMessage_(std::move(message)) {}

    bool ok() const { return error_ == ErrorCode::Success; }
    bool failed() const { return !ok(); }

    ErrorCode error() const { return error_; }
    const std::string& errorMessage() const { return errorMessage_; }

    explicit operator bool() const { return ok(); }

private:
    ErrorCode error_;
    std::string errorMessage_;
};

/**
 * @brief Base exception class for SDK errors
 */
class GatewayException : public std::runtime_error {
public:
    GatewayException(ErrorCode code, const std::string& message)
        : std::runtime_error(message), code_(code) {}

    ErrorCode code() const { return code_; }

private:
    ErrorCode code_;
};

/**
 * @brief Exception for connection errors
 */
class ConnectionException : public GatewayException {
public:
    ConnectionException(ErrorCode code, const std::string& message)
        : GatewayException(code, message) {}
};

/**
 * @brief Exception for authentication errors
 */
class AuthenticationException : public GatewayException {
public:
    AuthenticationException(ErrorCode code, const std::string& message)
        : GatewayException(code, message) {}
};

/**
 * @brief Exception for authorization errors
 */
class AuthorizationException : public GatewayException {
public:
    AuthorizationException(ErrorCode code, const std::string& message)
        : GatewayException(code, message) {}
};

/**
 * @brief Exception for protocol errors
 */
class ProtocolException : public GatewayException {
public:
    ProtocolException(ErrorCode code, const std::string& message)
        : GatewayException(code, message) {}
};

} // namespace gateway

// Enable std::error_code integration
namespace std {
    template<>
    struct is_error_code_enum<gateway::ErrorCode> : true_type {};
}
