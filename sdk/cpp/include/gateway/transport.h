/**
 * @file transport.h
 * @brief WebSocket transport interface for the Gateway Device SDK
 */

#pragma once

#include "types.h"
#include "error.h"
#include "config.h"
#include "logger.h"
#include <string>
#include <functional>
#include <memory>
#include <vector>

namespace gateway {

/**
 * @brief Transport connection events
 */
enum class TransportEvent {
    Connected,
    Disconnected,
    Error,
    MessageReceived
};

/**
 * @brief Transport state
 */
enum class TransportState {
    Disconnected,
    Connecting,
    Connected,
    Closing,
    Closed,
    Error
};

/**
 * @brief Callback types for transport events
 */
using TransportConnectedCallback = std::function<void()>;
using TransportDisconnectedCallback = std::function<void(ErrorCode code, const std::string& reason)>;
using TransportErrorCallback = std::function<void(ErrorCode code, const std::string& message)>;
using TransportMessageCallback = std::function<void(const std::string& message)>;

/**
 * @brief Transport interface for WebSocket communication
 *
 * This abstract interface allows different WebSocket implementations
 * to be used with the SDK (libwebsockets, boost::beast, etc.)
 */
class ITransport {
public:
    virtual ~ITransport() = default;

    /**
     * @brief Connect to the gateway
     * @param url WebSocket URL (ws:// or wss://)
     * @param timeout Connection timeout
     * @return Result indicating success or failure
     */
    virtual Result<void> connect(const std::string& url, Duration timeout) = 0;

    /**
     * @brief Disconnect from the gateway
     * @param code WebSocket close code
     * @param reason Close reason string
     */
    virtual void disconnect(int code = 1000, const std::string& reason = "") = 0;

    /**
     * @brief Send a text message
     * @param message Message to send
     * @return Result indicating success or failure
     */
    virtual Result<void> send(const std::string& message) = 0;

    /**
     * @brief Get current transport state
     */
    virtual TransportState getState() const = 0;

    /**
     * @brief Check if connected
     */
    virtual bool isConnected() const = 0;

    /**
     * @brief Process transport events (call in event loop)
     * @param timeout Max time to wait for events
     */
    virtual void poll(Duration timeout) = 0;

    /**
     * @brief Set callback for connection established
     */
    virtual void onConnected(TransportConnectedCallback callback) = 0;

    /**
     * @brief Set callback for disconnection
     */
    virtual void onDisconnected(TransportDisconnectedCallback callback) = 0;

    /**
     * @brief Set callback for errors
     */
    virtual void onError(TransportErrorCallback callback) = 0;

    /**
     * @brief Set callback for received messages
     */
    virtual void onMessage(TransportMessageCallback callback) = 0;
};

/**
 * @brief Factory function to create default transport
 */
std::unique_ptr<ITransport> createTransport(const TlsConfig& tlsConfig, Logger& logger);

/**
 * @brief WebSocket transport implementation using libwebsockets
 */
class WebSocketTransport : public ITransport {
public:
    WebSocketTransport(const TlsConfig& tlsConfig, Logger& logger);
    ~WebSocketTransport() override;

    // Non-copyable
    WebSocketTransport(const WebSocketTransport&) = delete;
    WebSocketTransport& operator=(const WebSocketTransport&) = delete;

    // Movable
    WebSocketTransport(WebSocketTransport&&) noexcept;
    WebSocketTransport& operator=(WebSocketTransport&&) noexcept;

    Result<void> connect(const std::string& url, Duration timeout) override;
    void disconnect(int code, const std::string& reason) override;
    Result<void> send(const std::string& message) override;
    TransportState getState() const override;
    bool isConnected() const override;
    void poll(Duration timeout) override;

    void onConnected(TransportConnectedCallback callback) override;
    void onDisconnected(TransportDisconnectedCallback callback) override;
    void onError(TransportErrorCallback callback) override;
    void onMessage(TransportMessageCallback callback) override;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace gateway
