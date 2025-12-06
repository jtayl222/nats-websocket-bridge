/**
 * @file reconnect_policy.cpp
 * @brief Reconnection policy implementation
 */

#include "gateway/reconnect_policy.h"
#include <algorithm>
#include <cmath>

namespace gateway {

ReconnectPolicy::ReconnectPolicy(const ReconnectConfig& config)
    : enabled_(config.enabled)
    , initialDelay_(config.initialDelay)
    , maxDelay_(config.maxDelay)
    , backoffMultiplier_(config.backoffMultiplier)
    , jitterEnabled_(config.jitterEnabled)
    , maxJitterFraction_(config.maxJitterFraction)
    , maxAttempts_(config.maxAttempts)
    , resubscribe_(config.resubscribeOnReconnect)
{}

ReconnectPolicy::ReconnectPolicy()
    : enabled_(true)
    , initialDelay_(Duration{1000})
    , maxDelay_(Duration{30000})
    , backoffMultiplier_(2.0)
    , jitterEnabled_(true)
    , maxJitterFraction_(0.25)
    , maxAttempts_(0)  // unlimited
    , resubscribe_(true)
{}

Duration ReconnectPolicy::getNextDelay() {
    if (!shouldReconnect()) {
        return Duration{0};
    }

    attemptCount_++;

    Duration delay = calculateDelay();

    if (jitterEnabled_) {
        delay = addJitter(delay);
    }

    return delay;
}

bool ReconnectPolicy::shouldReconnect() const {
    if (!enabled_) {
        return false;
    }

    if (maxAttempts_ > 0 && attemptCount_ >= maxAttempts_) {
        return false;
    }

    return true;
}

void ReconnectPolicy::reset() {
    attemptCount_ = 0;
}

Duration ReconnectPolicy::calculateDelay() const {
    if (attemptCount_ <= 1) {
        return initialDelay_;
    }

    // Exponential backoff: initialDelay * multiplier^(attempt - 1)
    double delayMs = initialDelay_.count() *
                     std::pow(backoffMultiplier_, attemptCount_ - 1);

    // Cap at max delay
    delayMs = std::min(delayMs, static_cast<double>(maxDelay_.count()));

    return Duration{static_cast<long long>(delayMs)};
}

Duration ReconnectPolicy::addJitter(Duration delay) const {
    if (!jitterEnabled_ || maxJitterFraction_ <= 0) {
        return delay;
    }

    // Add random jitter: delay +/- (delay * jitterFraction)
    std::uniform_real_distribution<double> dist(-maxJitterFraction_, maxJitterFraction_);

    double jitterFactor = 1.0 + dist(rng_);
    auto jitteredMs = static_cast<long long>(delay.count() * jitterFactor);

    // Ensure we don't go negative or exceed max
    jitteredMs = std::max(1LL, jitteredMs);
    jitteredMs = std::min(jitteredMs, static_cast<long long>(maxDelay_.count()));

    return Duration{jitteredMs};
}

// ReconnectTimer implementation
ReconnectTimer::ReconnectTimer(ReconnectPolicy& policy)
    : policy_(policy)
{}

Duration ReconnectTimer::start() {
    Duration delay = policy_.getNextDelay();

    if (delay.count() > 0) {
        expiryTime_ = std::chrono::steady_clock::now() + delay;
        active_ = true;
    } else {
        active_ = false;
    }

    return delay;
}

bool ReconnectTimer::isExpired() const {
    if (!active_) {
        return false;
    }

    return std::chrono::steady_clock::now() >= expiryTime_;
}

Duration ReconnectTimer::remaining() const {
    if (!active_) {
        return Duration{0};
    }

    auto now = std::chrono::steady_clock::now();
    if (now >= expiryTime_) {
        return Duration{0};
    }

    return std::chrono::duration_cast<Duration>(expiryTime_ - now);
}

void ReconnectTimer::cancel() {
    active_ = false;
}

} // namespace gateway
