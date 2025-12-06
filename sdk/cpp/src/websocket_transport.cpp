/**
 * @file websocket_transport.cpp
 * @brief WebSocket transport implementation using libwebsockets
 */

#include "gateway/transport.h"
#include <libwebsockets.h>
#include <queue>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <cstring>

namespace gateway {

// Internal implementation using libwebsockets
class WebSocketTransport::Impl {
public:
    Impl(const TlsConfig& tlsConfig, Logger& logger)
        : tlsConfig_(tlsConfig)
        , logger_(logger)
        , state_(TransportState::Disconnected)
        , context_(nullptr)
        , wsi_(nullptr)
        , shouldExit_(false)
    {}

    ~Impl() {
        disconnect(1000, "Destructor");
        destroyContext();
    }

    Result<void> connect(const std::string& url, Duration timeout) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ == TransportState::Connected || state_ == TransportState::Connecting) {
            return Result<void>(ErrorCode::AlreadyConnected, "Already connected or connecting");
        }

        // Parse URL
        if (!parseUrl(url)) {
            return Result<void>(ErrorCode::ConnectionFailed, "Invalid URL: " + url);
        }

        state_ = TransportState::Connecting;
        shouldExit_ = false;

        // Create context if needed
        if (!createContext()) {
            state_ = TransportState::Error;
            return Result<void>(ErrorCode::ConnectionFailed, "Failed to create WebSocket context");
        }

        // Create connection
        struct lws_client_connect_info connectInfo = {};
        connectInfo.context = context_;
        connectInfo.address = host_.c_str();
        connectInfo.port = port_;
        connectInfo.path = path_.c_str();
        connectInfo.host = host_.c_str();
        connectInfo.origin = host_.c_str();
        connectInfo.protocol = "gateway";
        connectInfo.userdata = this;

        if (useTls_) {
            connectInfo.ssl_connection = LCCSCF_USE_SSL;
            if (!tlsConfig_.verifyPeer) {
                connectInfo.ssl_connection |= LCCSCF_ALLOW_SELFSIGNED |
                                              LCCSCF_SKIP_SERVER_CERT_HOSTNAME_CHECK;
            }
        }

        wsi_ = lws_client_connect_via_info(&connectInfo);
        if (!wsi_) {
            state_ = TransportState::Error;
            return Result<void>(ErrorCode::ConnectionFailed, "Failed to initiate connection");
        }

        // Wait for connection with timeout
        auto startTime = std::chrono::steady_clock::now();
        while (state_ == TransportState::Connecting) {
            lws_service(context_, 50);

            auto elapsed = std::chrono::steady_clock::now() - startTime;
            if (elapsed > timeout) {
                disconnect(1000, "Connection timeout");
                return Result<void>(ErrorCode::ConnectionTimeout, "Connection timed out");
            }
        }

        if (state_ != TransportState::Connected) {
            return Result<void>(ErrorCode::ConnectionFailed, "Connection failed");
        }

        return Result<void>();
    }

    void disconnect(int code, const std::string& reason) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ == TransportState::Disconnected || state_ == TransportState::Closed) {
            return;
        }

        shouldExit_ = true;
        state_ = TransportState::Closing;

        if (wsi_) {
            lws_close_reason(wsi_, (lws_close_status)code,
                           (unsigned char*)reason.c_str(), reason.length());
            lws_callback_on_writable(wsi_);
        }

        state_ = TransportState::Closed;
        wsi_ = nullptr;

        if (disconnectedCallback_) {
            disconnectedCallback_(ErrorCode::Success, reason);
        }
    }

    Result<void> send(const std::string& message) {
        std::lock_guard<std::mutex> lock(mutex_);

        if (state_ != TransportState::Connected) {
            return Result<void>(ErrorCode::NotConnected, "Not connected");
        }

        // Queue message for sending
        sendQueue_.push(message);

        // Request write callback
        if (wsi_) {
            lws_callback_on_writable(wsi_);
        }

        return Result<void>();
    }

    TransportState getState() const {
        return state_.load();
    }

    bool isConnected() const {
        return state_ == TransportState::Connected;
    }

    void poll(Duration timeout) {
        if (context_) {
            lws_service(context_, static_cast<int>(timeout.count()));
        }
    }

    void onConnected(TransportConnectedCallback callback) {
        connectedCallback_ = std::move(callback);
    }

    void onDisconnected(TransportDisconnectedCallback callback) {
        disconnectedCallback_ = std::move(callback);
    }

    void onError(TransportErrorCallback callback) {
        errorCallback_ = std::move(callback);
    }

    void onMessage(TransportMessageCallback callback) {
        messageCallback_ = std::move(callback);
    }

    // LWS callback handler
    int handleCallback(struct lws* wsi, enum lws_callback_reasons reason,
                       void* in, size_t len) {
        switch (reason) {
            case LWS_CALLBACK_CLIENT_ESTABLISHED:
                logger_.info("Transport", "WebSocket connection established");
                state_ = TransportState::Connected;
                if (connectedCallback_) {
                    connectedCallback_();
                }
                break;

            case LWS_CALLBACK_CLIENT_CONNECTION_ERROR:
                logger_.error("Transport", std::string("Connection error: ") +
                             (in ? (const char*)in : "unknown"));
                state_ = TransportState::Error;
                if (errorCallback_) {
                    errorCallback_(ErrorCode::ConnectionFailed,
                                  in ? (const char*)in : "Connection error");
                }
                break;

            case LWS_CALLBACK_CLIENT_CLOSED:
                logger_.info("Transport", "WebSocket connection closed");
                state_ = TransportState::Closed;
                wsi_ = nullptr;
                if (disconnectedCallback_) {
                    disconnectedCallback_(ErrorCode::ConnectionClosed, "Connection closed");
                }
                break;

            case LWS_CALLBACK_CLIENT_RECEIVE: {
                if (in && len > 0) {
                    std::string message((const char*)in, len);
                    logger_.trace("Transport", "Received: " + message);
                    if (messageCallback_) {
                        messageCallback_(message);
                    }
                }
                break;
            }

            case LWS_CALLBACK_CLIENT_WRITEABLE: {
                std::lock_guard<std::mutex> lock(mutex_);

                if (shouldExit_) {
                    return -1;  // Close connection
                }

                if (!sendQueue_.empty()) {
                    const std::string& message = sendQueue_.front();

                    // Allocate buffer with LWS pre-padding
                    size_t bufLen = LWS_PRE + message.length();
                    std::vector<unsigned char> buf(bufLen);

                    memcpy(&buf[LWS_PRE], message.c_str(), message.length());

                    int written = lws_write(wsi, &buf[LWS_PRE], message.length(),
                                           LWS_WRITE_TEXT);

                    if (written < 0) {
                        logger_.error("Transport", "Failed to send message");
                        if (errorCallback_) {
                            errorCallback_(ErrorCode::InternalError, "Write failed");
                        }
                    } else {
                        logger_.trace("Transport", "Sent: " + message);
                        sendQueue_.pop();
                    }

                    // More messages to send?
                    if (!sendQueue_.empty()) {
                        lws_callback_on_writable(wsi);
                    }
                }
                break;
            }

            default:
                break;
        }

        return 0;
    }

private:
    bool parseUrl(const std::string& url) {
        // Parse ws:// or wss://
        size_t schemeEnd = url.find("://");
        if (schemeEnd == std::string::npos) {
            return false;
        }

        std::string scheme = url.substr(0, schemeEnd);
        if (scheme == "wss") {
            useTls_ = true;
            port_ = 443;
        } else if (scheme == "ws") {
            useTls_ = false;
            port_ = 80;
        } else {
            return false;
        }

        size_t hostStart = schemeEnd + 3;
        size_t pathStart = url.find('/', hostStart);
        size_t portStart = url.find(':', hostStart);

        if (portStart != std::string::npos && (pathStart == std::string::npos || portStart < pathStart)) {
            host_ = url.substr(hostStart, portStart - hostStart);
            size_t portEnd = (pathStart != std::string::npos) ? pathStart : url.length();
            port_ = std::stoi(url.substr(portStart + 1, portEnd - portStart - 1));
        } else {
            size_t hostEnd = (pathStart != std::string::npos) ? pathStart : url.length();
            host_ = url.substr(hostStart, hostEnd - hostStart);
        }

        path_ = (pathStart != std::string::npos) ? url.substr(pathStart) : "/";

        logger_.debug("Transport", "Parsed URL - Host: " + host_ +
                     ", Port: " + std::to_string(port_) +
                     ", Path: " + path_ +
                     ", TLS: " + (useTls_ ? "yes" : "no"));

        return true;
    }

    bool createContext() {
        if (context_) {
            return true;
        }

        struct lws_context_creation_info info = {};
        info.port = CONTEXT_PORT_NO_LISTEN;
        info.protocols = protocols_;
        info.gid = -1;
        info.uid = -1;
        info.user = this;

        if (useTls_) {
            info.options |= LWS_SERVER_OPTION_DO_SSL_GLOBAL_INIT;

            if (!tlsConfig_.caCertPath.empty()) {
                info.client_ssl_ca_filepath = tlsConfig_.caCertPath.c_str();
            }

            if (!tlsConfig_.clientCertPath.empty()) {
                info.client_ssl_cert_filepath = tlsConfig_.clientCertPath.c_str();
            }

            if (!tlsConfig_.clientKeyPath.empty()) {
                info.client_ssl_private_key_filepath = tlsConfig_.clientKeyPath.c_str();
            }
        }

        context_ = lws_create_context(&info);
        return context_ != nullptr;
    }

    void destroyContext() {
        if (context_) {
            lws_context_destroy(context_);
            context_ = nullptr;
        }
    }

    // Static callback for libwebsockets
    static int lwsCallback(struct lws* wsi, enum lws_callback_reasons reason,
                          void* user, void* in, size_t len) {
        struct lws_context* context = lws_get_context(wsi);
        Impl* impl = static_cast<Impl*>(lws_context_user(context));

        if (impl) {
            return impl->handleCallback(wsi, reason, in, len);
        }

        return 0;
    }

    // Protocol definition
    static constexpr struct lws_protocols protocols_[] = {
        {
            "gateway",
            lwsCallback,
            0,
            65536,  // rx buffer size
            0,
            nullptr,
            0
        },
        { nullptr, nullptr, 0, 0, 0, nullptr, 0 }
    };

    TlsConfig tlsConfig_;
    Logger& logger_;
    std::atomic<TransportState> state_;

    struct lws_context* context_;
    struct lws* wsi_;

    std::string host_;
    int port_ = 0;
    std::string path_;
    bool useTls_ = false;

    std::mutex mutex_;
    std::queue<std::string> sendQueue_;
    std::atomic<bool> shouldExit_;

    TransportConnectedCallback connectedCallback_;
    TransportDisconnectedCallback disconnectedCallback_;
    TransportErrorCallback errorCallback_;
    TransportMessageCallback messageCallback_;
};

// Define static member
constexpr struct lws_protocols WebSocketTransport::Impl::protocols_[];

// WebSocketTransport implementation
WebSocketTransport::WebSocketTransport(const TlsConfig& tlsConfig, Logger& logger)
    : impl_(std::make_unique<Impl>(tlsConfig, logger))
{}

WebSocketTransport::~WebSocketTransport() = default;

WebSocketTransport::WebSocketTransport(WebSocketTransport&&) noexcept = default;
WebSocketTransport& WebSocketTransport::operator=(WebSocketTransport&&) noexcept = default;

Result<void> WebSocketTransport::connect(const std::string& url, Duration timeout) {
    return impl_->connect(url, timeout);
}

void WebSocketTransport::disconnect(int code, const std::string& reason) {
    impl_->disconnect(code, reason);
}

Result<void> WebSocketTransport::send(const std::string& message) {
    return impl_->send(message);
}

TransportState WebSocketTransport::getState() const {
    return impl_->getState();
}

bool WebSocketTransport::isConnected() const {
    return impl_->isConnected();
}

void WebSocketTransport::poll(Duration timeout) {
    impl_->poll(timeout);
}

void WebSocketTransport::onConnected(TransportConnectedCallback callback) {
    impl_->onConnected(std::move(callback));
}

void WebSocketTransport::onDisconnected(TransportDisconnectedCallback callback) {
    impl_->onDisconnected(std::move(callback));
}

void WebSocketTransport::onError(TransportErrorCallback callback) {
    impl_->onError(std::move(callback));
}

void WebSocketTransport::onMessage(TransportMessageCallback callback) {
    impl_->onMessage(std::move(callback));
}

// Factory function
std::unique_ptr<ITransport> createTransport(const TlsConfig& tlsConfig, Logger& logger) {
    return std::make_unique<WebSocketTransport>(tlsConfig, logger);
}

} // namespace gateway
