# Class Diagrams

## 1. Gateway Services

```mermaid
classDiagram
    class IJwtDeviceAuthService {
        <<interface>>
        +ValidateToken(token) DeviceAuthResult
        +CanPublish(context, subject) bool
        +CanSubscribe(context, subject) bool
        +GenerateToken(clientId, role, publish, subscribe, expiry) string
    }

    class JwtDeviceAuthService {
        -_logger: ILogger
        -_options: JwtOptions
        -_validationParameters: TokenValidationParameters
        -_tokenHandler: JwtSecurityTokenHandler
        +ValidateToken(token) DeviceAuthResult
        +CanPublish(context, subject) bool
        +CanSubscribe(context, subject) bool
        +GenerateToken(clientId, role, publish, subscribe, expiry) string
        -ExtractDeviceContext(principal, token) DeviceContext
        -ParseTopicList(claim) IReadOnlyList~string~
        -MatchesSubject(pattern, subject)$ bool
    }

    class DeviceAuthResult {
        +IsSuccess: bool
        +Context: DeviceContext?
        +Error: string?
        +Success(context)$ DeviceAuthResult
        +Failure(error)$ DeviceAuthResult
    }

    class IMessageValidationService {
        <<interface>>
        +ValidateMessage(message) ValidationResult
    }

    class MessageValidationService {
        -_options: GatewayOptions
        +ValidateMessage(message) ValidationResult
        -ValidateSubject(subject) bool
        -ValidatePayload(payload) bool
    }

    class IMessageThrottlingService {
        <<interface>>
        +TryAcquire(deviceId) bool
        +GetRemainingTokens(deviceId) int
    }

    class TokenBucketThrottlingService {
        -_buckets: ConcurrentDictionary
        -_options: GatewayOptions
        +TryAcquire(deviceId) bool
    }

    IJwtDeviceAuthService <|.. JwtDeviceAuthService
    JwtDeviceAuthService ..> DeviceAuthResult
    IMessageValidationService <|.. MessageValidationService
    IMessageThrottlingService <|.. TokenBucketThrottlingService
```

## 2. Connection Management

```mermaid
classDiagram
    class IDeviceConnectionManager {
        <<interface>>
        +RegisterDevice(deviceId, connection)
        +UnregisterDevice(deviceId)
        +GetConnection(deviceId) DeviceConnection
        +GetAllConnections() IEnumerable
        +UpdateActivity(deviceId)
    }

    class DeviceConnectionManager {
        -_connections: ConcurrentDictionary
        -_logger: ILogger
        +RegisterDevice(deviceId, connection)
        +UnregisterDevice(deviceId)
        +GetConnection(deviceId) DeviceConnection
    }

    class DeviceConnection {
        +Context: DeviceContext
        +WebSocket: WebSocket
    }

    class IMessageBufferService {
        <<interface>>
        +Enqueue(deviceId, message)
        +Dequeue(deviceId) GatewayMessage
        +GetPendingCount(deviceId) int
    }

    class ChannelMessageBufferService {
        -_channels: ConcurrentDictionary
        -_options: GatewayOptions
        +Enqueue(deviceId, message)
        +Dequeue(deviceId) GatewayMessage
    }

    IDeviceConnectionManager <|.. DeviceConnectionManager
    DeviceConnectionManager --> DeviceConnection
    IMessageBufferService <|.. ChannelMessageBufferService
```

## 3. NATS Services

```mermaid
classDiagram
    class IJetStreamNatsService {
        <<interface>>
        +IsConnected: bool
        +IsJetStreamAvailable: bool
        +InitializeAsync(token)
        +PublishAsync(subject, data, headers, messageId) JetStreamPublishResult
        +SubscribeAsync(stream, consumer, handler) JetStreamSubscription
        +SubscribeDeviceAsync(deviceId, subject, handler, replayOptions) JetStreamSubscription
        +UnsubscribeAsync(subscriptionId, deleteConsumer)
        +AckMessageAsync(message)
        +NakMessageAsync(message, delay)
        +EnsureStreamExistsAsync(config) StreamInfo
        +CreateConsumerAsync(config) ConsumerInfo
        +GetOrCreateConsumerAsync(config) ConsumerInfo
        +FetchMessagesAsync(stream, consumer, batchSize, timeout) List~JetStreamMessage~
    }

    class JetStreamNatsService {
        -_connection: IConnection
        -_jetStream: IJetStream
        -_options: JetStreamOptions
        -_subscriptions: ConcurrentDictionary
        +InitializeAsync(token)
        +PublishAsync(subject, data, headers, messageId) JetStreamPublishResult
        +SubscribeDeviceAsync(deviceId, subject, handler, replayOptions) JetStreamSubscription
        +EnsureStreamExistsAsync(config) StreamInfo
        +CreateConsumerAsync(config) ConsumerInfo
    }

    class JetStreamInitializationService {
        <<IHostedService>>
        -_jetStreamService: IJetStreamNatsService
        -_options: JetStreamOptions
        +StartAsync(token)
        +StopAsync(token)
    }

    class JetStreamPublishResult {
        +Success: bool
        +Stream: string
        +Sequence: ulong
        +Duplicate: bool
        +Error: string
        +RetryCount: int
    }

    class JetStreamMessage {
        +Subject: string
        +Data: byte[]
        +Headers: Dictionary
        +Sequence: ulong
        +ConsumerSequence: ulong
        +Timestamp: DateTime
        +DeliveryCount: int
        +IsRedelivered: bool
        +Stream: string
        +Consumer: string
    }

    class JetStreamSubscription {
        +SubscriptionId: string
        +ConsumerName: string
        +StreamName: string
        +Subject: string
        +IsActive: bool
        +DeviceId: string
    }

    IJetStreamNatsService <|.. JetStreamNatsService
    JetStreamInitializationService --> IJetStreamNatsService
    JetStreamNatsService ..> JetStreamPublishResult
    JetStreamNatsService ..> JetStreamMessage
    JetStreamNatsService ..> JetStreamSubscription
```

## 4. Message Models

```mermaid
classDiagram
    class GatewayMessage {
        +Type: MessageType
        +Subject: string
        +Payload: JsonElement?
        +CorrelationId: string?
        +Timestamp: DateTime
        +DeviceId: string?
    }

    class MessageType {
        <<enumeration>>
        Publish = 0
        Subscribe = 1
        Unsubscribe = 2
        Message = 3
        Request = 4
        Reply = 5
        Ack = 6
        Error = 7
        Auth = 8
        Ping = 9
        Pong = 10
    }

    class DeviceContext {
        +ClientId: string
        +Role: string
        +AllowedPublish: IReadOnlyList~string~
        +AllowedSubscribe: IReadOnlyList~string~
        +ExpiresAt: DateTime
        +ConnectedAt: DateTime
        +IsExpired: bool
    }

    class NatsJwtClaims {
        <<static>>
        +Role: string = "role"
        +Publish: string = "pub"
        +Subscribe: string = "subscribe"
    }

    GatewayMessage --> MessageType
```

## 5. C++ SDK Classes

```mermaid
classDiagram
    class GatewayClient {
        -impl_: unique_ptr~Impl~
        +GatewayClient(config)
        +connect() bool
        +connectAsync() Result~void~
        +disconnect()
        +isConnected() bool
        +getState() ConnectionState
        +publish(subject, payload) Result~void~
        +subscribe(subject, handler) Result~SubscriptionId~
        +unsubscribe(id) Result~void~
        +poll(timeout)
        +run()
        +runAsync() bool
        +stop()
        +onConnected(callback)
        +onDisconnected(callback)
        +onError(callback)
        +getStats() ClientStats
    }

    class GatewayConfig {
        +gatewayUrl: string
        +deviceId: string
        +authToken: string
        +deviceType: DeviceType
        +connectTimeout: Duration
        +authTimeout: Duration
        +tls: TlsConfig
        +reconnect: ReconnectConfig
        +heartbeat: HeartbeatConfig
        +buffer: BufferConfig
        +isValid() bool
    }

    class ITransport {
        <<interface>>
        +connect(url, timeout) Result~void~
        +disconnect(code, reason)
        +send(message) Result~void~
        +getState() TransportState
        +isConnected() bool
        +poll(timeout)
        +onConnected(callback)
        +onDisconnected(callback)
        +onMessage(callback)
    }

    class WebSocketTransport {
        -impl_: unique_ptr~Impl~
        +connect(url, timeout) Result~void~
        +disconnect(code, reason)
        +send(message) Result~void~
    }

    class Protocol {
        <<static>>
        +serialize(message) string
        +deserialize(json) Result~Message~
        +serializeAuthRequest(request) string
        +deserializeAuthResponse(json) Result~AuthResponse~
        +isValidSubject(subject) bool
        +getTimestamp() string
    }

    class AuthManager {
        -state_: AuthState
        -deviceContext_: optional~DeviceContext~
        +createAuthRequest(config) Message
        +processAuthResponse(message) AuthResult
        +startAuth(config, callback)
        +handleMessage(message) bool
        +isAuthenticated() bool
        +canPublish(subject) bool
        +canSubscribe(subject) bool
    }

    class ReconnectPolicy {
        -enabled_: bool
        -initialDelay_: Duration
        -maxDelay_: Duration
        -backoffMultiplier_: double
        -attemptCount_: uint32
        +getNextDelay() Duration
        +shouldReconnect() bool
        +reset()
    }

    GatewayClient --> GatewayConfig
    GatewayClient --> ITransport
    GatewayClient --> AuthManager
    GatewayClient --> ReconnectPolicy
    ITransport <|.. WebSocketTransport
    GatewayClient ..> Protocol
```

## 6. SDK Message Types

```mermaid
classDiagram
    class Message {
        +type: MessageType
        +subject: string
        +payload: JsonValue
        +correlationId: optional~string~
        +timestamp: optional~Timestamp~
        +deviceId: optional~string~
        +publish(subject, payload)$ Message
        +subscribe(subject)$ Message
        +unsubscribe(subject)$ Message
        +ping()$ Message
        +pong()$ Message
    }

    class JsonValue {
        -type_: Type
        -boolValue_: bool
        -intValue_: int64
        -doubleValue_: double
        -stringValue_: string
        -arrayValue_: Array
        -objectValue_: Object
        +isNull() bool
        +isBool() bool
        +isInt() bool
        +isDouble() bool
        +isString() bool
        +isArray() bool
        +isObject() bool
        +asBool() bool
        +asInt() int64
        +asDouble() double
        +asString() string
        +operator[](key) JsonValue
        +object()$ JsonValue
        +array()$ JsonValue
    }

    class MessageType {
        <<enumeration>>
        Publish = 0
        Subscribe = 1
        Unsubscribe = 2
        Message = 3
        Request = 4
        Reply = 5
        Ack = 6
        Error = 7
        Auth = 8
        Ping = 9
        Pong = 10
    }

    class ConnectionState {
        <<enumeration>>
        Disconnected
        Connecting
        Authenticating
        Connected
        Reconnecting
        Closing
        Closed
    }

    class ErrorCode {
        <<enumeration>>
        Success = 0
        ConnectionFailed = 100
        ConnectionTimeout = 101
        AuthenticationFailed = 200
        NotAuthorized = 300
        InvalidMessage = 400
        PayloadTooLarge = 403
        NotConnected = 503
        RateLimitExceeded = 506
    }

    Message --> MessageType
    Message --> JsonValue
```

## 7. SDK Result Types

```mermaid
classDiagram
    class Result~T~ {
        -value_: T
        -error_: ErrorCode
        -errorMessage_: string
        +Result(value)
        +Result(error, message)
        +ok() bool
        +failed() bool
        +value() T
        +error() ErrorCode
        +errorMessage() string
        +operator bool()
    }

    class GatewayException {
        -code_: ErrorCode
        +GatewayException(code, message)
        +code() ErrorCode
    }

    class ConnectionException {
        +ConnectionException(code, message)
    }

    class AuthenticationException {
        +AuthenticationException(code, message)
    }

    class AuthorizationException {
        +AuthorizationException(code, message)
    }

    class ProtocolException {
        +ProtocolException(code, message)
    }

    GatewayException <|-- ConnectionException
    GatewayException <|-- AuthenticationException
    GatewayException <|-- AuthorizationException
    GatewayException <|-- ProtocolException
```

## 8. Configuration Classes

```mermaid
classDiagram
    class GatewayOptions {
        +MaxMessageSize: int
        +MessageRateLimitPerSecond: int
        +OutgoingBufferSize: int
        +AuthenticationTimeoutSeconds: int
        +PingIntervalSeconds: int
        +PingTimeoutSeconds: int
    }

    class NatsOptions {
        +Url: string
        +Name: string
        +AllowReconnect: bool
        +MaxReconnectAttempts: int
        +ReconnectWait: TimeSpan
        +ConnectionTimeout: TimeSpan
        +JetStreamEnabled: bool
        +StreamName: string
    }

    class JetStreamOptions {
        +Streams: List~StreamConfig~
        +DefaultRetentionDays: int
        +DefaultStorageType: string
    }

    class StreamConfig {
        +Name: string
        +Subjects: List~string~
        +Retention: string
        +MaxAge: TimeSpan
        +Storage: string
        +Replicas: int
        +DenyDelete: bool
        +DenyPurge: bool
    }

    JetStreamOptions --> StreamConfig
```

## 9. Demo Device Classes

```mermaid
classDiagram
    class DemoConfig {
        +gatewayUrl: string
        +insecure: bool
        +lineId: string
        +lineName: string
        +batchId: string
        +product: string
        +lotNumber: string
        +targetCount: int
    }

    class SimulatedValue {
        -baseValue_: double
        -noiseStddev_: double
        -driftRate_: double
        -anomalyMagnitude_: double
        +read() double
        +setBase(value)
        +injectAnomaly(magnitude, duration)
    }

    class ConveyorState {
        <<demo>>
        -mode_: Mode
        -currentSpeed_: double
        -targetSpeed_: double
        +start() bool
        +stop() bool
        +setSpeed(speed) bool
        +emergencyStop()
        +reset() bool
        +update(deltaSeconds)
        +getMode() Mode
        +getCurrentSpeed() double
    }

    class VisionScanner {
        <<demo>>
        -defectRate_: double
        -totalScans_: int
        -passCount_: int
        -rejectCount_: int
        +scan() ScanResult
        +setDefectRate(rate)
        +setHighDefectMode(enabled, rate)
        +getYield() double
    }

    class OEECalculator {
        <<demo>>
        -plannedTime_: double
        -downtime_: double
        -goodCount_: int
        -totalCount_: int
        +updateProduction(good, total, runtime)
        +getAvailability() double
        +getPerformance() double
        +getQuality() double
        +getOEE() double
    }

    class LineOrchestrator {
        <<demo>>
        -state_: LineState
        -devices_: map
        -oee_: OEECalculator
        +updateDeviceStatus(id, status)
        +setLineState(state)
        +allDevicesOnline() bool
        +getStatusSummary() JsonValue
    }

    LineOrchestrator --> OEECalculator
    LineOrchestrator --> ConveyorState
```
