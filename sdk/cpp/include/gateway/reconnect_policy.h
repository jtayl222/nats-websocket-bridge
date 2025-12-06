/**
 * @file reconnect_policy.h
 * @brief Reconnection policy for the Gateway Device SDK
 */

#pragma once

#include "types.h"
#include "config.h"
#include <random>
#include <chrono>

namespace gateway {

/**
 * @brief Reconnection policy with exponential backoff and jitter
 *
 * Implements reconnection delay calculation with:
 * - Exponential backoff
 * - Optional jitter to prevent thundering herd
 * - Maximum delay cap
 * - Attempt limiting
 */
class ReconnectPolicy {
public:
    /**
     * @brief Create policy from configuration
     */
    explicit ReconnectPolicy(const ReconnectConfig& config);

    /**
     * @brief Default constructor with sensible defaults
     */
    ReconnectPolicy();

    /**
     * @brief Get the delay before the next reconnect attempt
     * @return Delay duration, or 0 if max attempts exceeded
     */
    Duration getNextDelay();

    /**
     * @brief Check if more attempts are allowed
     * @return true if reconnection should be attempted
     */
    bool shouldReconnect() const;

    /**
     * @brief Reset the policy (call after successful connection)
     */
    void reset();

    /**
     * @brief Get current attempt number (1-based)
     */
    uint32_t getAttemptCount() const { return attemptCount_; }

    /**
     * @brief Check if reconnection is enabled
     */
    bool isEnabled() const { return enabled_; }

    /**
     * @brief Enable or disable reconnection
     */
    void setEnabled(bool enabled) { enabled_ = enabled; }

    /**
     * @brief Check if resubscription should happen after reconnect
     */
    bool shouldResubscribe() const { return resubscribe_; }

private:
    Duration calculateDelay() const;
    Duration addJitter(Duration delay) const;

    bool enabled_;
    Duration initialDelay_;
    Duration maxDelay_;
    double backoffMultiplier_;
    bool jitterEnabled_;
    double maxJitterFraction_;
    uint32_t maxAttempts_;
    bool resubscribe_;

    uint32_t attemptCount_ = 0;
    mutable std::mt19937 rng_{std::random_device{}()};
};

/**
 * @brief Helper class for managing reconnection timing
 */
class ReconnectTimer {
public:
    explicit ReconnectTimer(ReconnectPolicy& policy);

    /**
     * @brief Start the reconnection timer
     * @return Delay until next reconnection attempt
     */
    Duration start();

    /**
     * @brief Check if timer has expired
     */
    bool isExpired() const;

    /**
     * @brief Get remaining time until expiry
     */
    Duration remaining() const;

    /**
     * @brief Cancel the timer
     */
    void cancel();

    /**
     * @brief Check if timer is active
     */
    bool isActive() const { return active_; }

private:
    ReconnectPolicy& policy_;
    std::chrono::steady_clock::time_point expiryTime_;
    bool active_ = false;
};

} // namespace gateway
