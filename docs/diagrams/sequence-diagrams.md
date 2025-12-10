# Sequence Diagrams

## 1. Connection & Authentication

### 1.1 Successful Connection Flow

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device<br/>(C++ SDK)
    participant WS as WebSocket<br/>Middleware
    participant Handler as Device<br/>WebSocket Handler
    participant Auth as JWT Auth<br/>Service
    participant ConnMgr as Connection<br/>Manager
    participant NATS as NATS<br/>JetStream

    Device->>+WS: WebSocket CONNECT /ws
    WS->>WS: Accept WebSocket
    WS->>+Handler: HandleConnectionAsync()
    Handler->>Handler: Start receive loop
    Handler-->>Device: WebSocket ACCEPTED

    Note over Device,Handler: Authentication Phase (30s timeout)

    Device->>Handler: {"type": 8, "payload": {"token": "<JWT>"}}

    Handler->>+Auth: ValidateToken(jwt)
    Auth->>Auth: Verify JWT signature
    Auth->>Auth: Extract claims (clientId, role, pub, subscribe)
    Auth-->>-Handler: DeviceContext {ClientId, Role, AllowedPublish, AllowedSubscribe}

    Handler->>+ConnMgr: RegisterDevice(context, websocket)
    ConnMgr->>ConnMgr: Store connection
    ConnMgr-->>-Handler: OK

    Handler-->>Device: {"type": 8, "payload": {"success": true, "clientId": "sensor-001", "role": "sensor"}}

    Note over Device,NATS: Device is now authenticated and can publish/subscribe

    loop Heartbeat (every 30s)
        Device->>Handler: {"type": 9} (Ping)
        Handler-->>Device: {"type": 10} (Pong)
    end
```

### 1.2 Failed Authentication

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant Auth as JWT Auth Service

    Device->>Handler: WebSocket CONNECT
    Handler-->>Device: WebSocket ACCEPTED

    Device->>Handler: {"type": 8, "payload": {"token": "<invalid-jwt>"}}

    Handler->>+Auth: ValidateToken(jwt)
    Auth->>Auth: Verify JWT signature
    Auth-->>-Handler: null (validation failed)

    Handler-->>Device: {"type": 8, "payload": {"success": false, "error": "Token validation failed"}}
    Handler->>Handler: Close WebSocket (1008)
    Handler--xDevice: WebSocket CLOSED
```

### 1.3 Authentication Timeout

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler

    Device->>Handler: WebSocket CONNECT
    Handler-->>Device: WebSocket ACCEPTED
    Handler->>Handler: Start auth timeout (30s)

    Note over Device,Handler: Device fails to send auth request...

    Handler->>Handler: Auth timeout expires

    Handler-->>Device: {"type": 7, "payload": {"message": "Authentication timeout"}}
    Handler->>Handler: Close WebSocket (1008)
    Handler--xDevice: WebSocket CLOSED
```

---

## 2. Publish Flow

### 2.1 Successful Publish to JetStream

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant Validate as Validation<br/>Service
    participant AuthZ as Authorization<br/>Service
    participant Throttle as Throttling<br/>Service
    participant NATS as NATS Service
    participant JS as JetStream

    Device->>Handler: {"type": 0, "subject": "factory.line1.temp", "payload": {"value": 72.3}}

    Handler->>+Validate: ValidateMessage(msg)
    Validate->>Validate: Check subject format
    Validate->>Validate: Check payload size (<1MB)
    Validate-->>-Handler: Valid

    Handler->>+AuthZ: CanPublish(deviceId, subject)
    AuthZ->>AuthZ: Match against allowedPublishTopics
    AuthZ-->>-Handler: true

    Handler->>+Throttle: TryAcquire(deviceId)
    Throttle->>Throttle: Token bucket check
    Throttle-->>-Handler: true

    Handler->>+NATS: PublishToJetStreamAsync(subject, payload)
    NATS->>+JS: Publish
    JS->>JS: Store message
    JS-->>-NATS: PubAck {stream, seq}
    NATS-->>-Handler: OK

    Note over Device,JS: Message persisted in JetStream stream
```

### 2.2 Publish Denied - Not Authorized

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant AuthZ as Authorization Service

    Device->>Handler: {"type": 0, "subject": "admin.config", "payload": {...}}

    Handler->>+AuthZ: CanPublish(deviceId, "admin.config")
    AuthZ->>AuthZ: Check allowedPublishTopics
    Note over AuthZ: Device only allowed: factory.line1.*
    AuthZ-->>-Handler: false

    Handler-->>Device: {"type": 7, "subject": "admin.config", "payload": {"message": "Not authorized to publish", "code": "NOT_AUTHORIZED"}}
```

### 2.3 Publish Denied - Rate Limited

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant Throttle as Throttling Service

    loop Burst of messages
        Device->>Handler: {"type": 0, ...} (message 1)
        Device->>Handler: {"type": 0, ...} (message 2)
        Device->>Handler: {"type": 0, ...} (message N)
    end

    Handler->>+Throttle: TryAcquire(deviceId)
    Throttle->>Throttle: Token bucket empty
    Throttle-->>-Handler: false (rate limited)

    Handler-->>Device: {"type": 7, "payload": {"message": "Rate limit exceeded", "code": "RATE_LIMIT"}}
```

---

## 3. Subscribe Flow

### 3.1 Successful Subscription

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant AuthZ as Authorization Service
    participant NATS as NATS Service
    participant JS as JetStream
    participant Buffer as Message Buffer

    Device->>Handler: {"type": 1, "subject": "factory.line1.conveyor.cmd"}

    Handler->>+AuthZ: CanSubscribe(deviceId, subject)
    AuthZ-->>-Handler: true

    Handler->>+NATS: SubscribeAsync(subject, callback)
    NATS->>+JS: Create consumer
    JS-->>-NATS: Consumer created
    NATS-->>-Handler: Subscription handle

    Handler-->>Device: {"type": 6, "subject": "factory.line1.conveyor.cmd", "payload": {"success": true}}

    Note over Device,JS: Later, a message arrives...

    JS->>NATS: Message on factory.line1.conveyor.cmd
    NATS->>Handler: Subscription callback
    Handler->>+Buffer: Enqueue(message)
    Buffer-->>-Handler: OK

    Handler->>Handler: SendBufferedMessagesAsync()
    Handler-->>Device: {"type": 3, "subject": "factory.line1.conveyor.cmd", "payload": {"action": "start"}}
```

### 3.2 Wildcard Subscription

```mermaid
sequenceDiagram
    autonumber
    participant Device as Orchestrator
    participant Handler as WebSocket Handler
    participant NATS as NATS Service
    participant JS as JetStream

    Device->>Handler: {"type": 1, "subject": "factory.line1.>"}

    Note over Handler: Wildcard ">" matches all tokens

    Handler->>+NATS: SubscribeAsync("factory.line1.>", callback)
    NATS->>JS: Create consumer with filter
    NATS-->>-Handler: Subscription

    Handler-->>Device: {"type": 6, "subject": "factory.line1.>", "payload": {"success": true}}

    Note over Device,JS: Receives messages from multiple subjects

    JS->>NATS: factory.line1.temp
    NATS->>Handler: callback
    Handler-->>Device: {"type": 3, "subject": "factory.line1.temp", ...}

    JS->>NATS: factory.line1.conveyor.status
    NATS->>Handler: callback
    Handler-->>Device: {"type": 3, "subject": "factory.line1.conveyor.status", ...}

    JS->>NATS: factory.line1.quality.rejects
    NATS->>Handler: callback
    Handler-->>Device: {"type": 3, "subject": "factory.line1.quality.rejects", ...}
```

### 3.3 Unsubscribe

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant Handler as WebSocket Handler
    participant NATS as NATS Service

    Device->>Handler: {"type": 2, "subject": "factory.line1.conveyor.cmd"}

    Handler->>Handler: Lookup subscription by subject
    Handler->>+NATS: UnsubscribeAsync(subscription)
    NATS->>NATS: Close subscription
    NATS-->>-Handler: OK

    Handler-->>Device: {"type": 6, "subject": "factory.line1.conveyor.cmd", "payload": {"success": true, "message": "Unsubscribed"}}
```

---

## 4. Emergency Broadcast (Fan-out)

### 4.1 E-Stop Broadcast to All Devices

```mermaid
sequenceDiagram
    autonumber
    participant EStop as E-Stop Button
    participant GW as Gateway
    participant JS as JetStream
    participant Conv as Conveyor
    participant Vision as Vision Scanner
    participant Counter as Counter
    participant Orch as Orchestrator

    Note over EStop: Operator presses E-Stop

    EStop->>GW: {"type": 0, "subject": "factory.line1.emergency", "payload": {"type": "emergency_stop", "action": "STOP_ALL"}}

    GW->>JS: Publish to factory.line1.emergency
    JS->>JS: Store message

    par Fan-out to all subscribers
        JS->>GW: Deliver to Conv subscription
        GW-->>Conv: {"type": 3, "subject": "factory.line1.emergency", ...}
        Conv->>Conv: emergencyStop()
    and
        JS->>GW: Deliver to Vision subscription
        GW-->>Vision: {"type": 3, "subject": "factory.line1.emergency", ...}
        Vision->>Vision: stopScanning()
    and
        JS->>GW: Deliver to Counter subscription
        GW-->>Counter: {"type": 3, "subject": "factory.line1.emergency", ...}
        Counter->>Counter: pauseCounting()
    and
        JS->>GW: Deliver to Orch subscription
        GW-->>Orch: {"type": 3, "subject": "factory.line1.emergency", ...}
        Orch->>Orch: setLineState(EMERGENCY)
    end

    Note over EStop,Orch: All devices halted within milliseconds
```

---

## 5. Reconnection Flow

### 5.1 Device Reconnection with State Replay

```mermaid
sequenceDiagram
    autonumber
    participant Device as Conveyor<br/>Controller
    participant SDK as C++ SDK
    participant GW as Gateway
    participant JS as JetStream

    Note over Device,JS: Connection lost (network issue)

    Device--xGW: WebSocket CLOSED

    SDK->>SDK: Detect disconnection
    SDK->>SDK: Start reconnect timer

    loop Exponential backoff
        SDK->>SDK: Wait 1s, 2s, 4s, 8s...
        SDK->>GW: WebSocket CONNECT
        Note right of SDK: Connection attempts
    end

    GW-->>SDK: WebSocket ACCEPTED

    SDK->>GW: {"type": 8, "payload": {"token": "<JWT>"}}
    GW-->>SDK: {"type": 8, "payload": {"success": true, "clientId": "actuator-conveyor-001"}}

    SDK->>SDK: Resubscribe to previous topics

    SDK->>GW: {"type": 1, "subject": "factory.line1.conveyor.cmd"}

    Note over GW,JS: JetStream replays missed messages

    GW->>+JS: Create durable consumer (resume from last ack)
    JS-->>-GW: Consumer with pending messages

    JS->>GW: Replay: {"action": "setSpeed", "value": 120}
    GW-->>Device: {"type": 3, ...}

    JS->>GW: Replay: {"action": "start"}
    GW-->>Device: {"type": 3, ...}

    Device->>Device: Apply replayed state
    Note over Device: Conveyor resumes at 120 units/min
```

### 5.2 Gateway Restart - Consumer Resume

```mermaid
sequenceDiagram
    autonumber
    participant Device as Device
    participant GW as Gateway (Old)
    participant GW2 as Gateway (New)
    participant JS as JetStream

    Note over GW: Gateway crashes or restarts

    Device--xGW: Connection lost

    Note over JS: Messages continue to accumulate

    JS->>JS: Store: factory.line1.temp (seq 101)
    JS->>JS: Store: factory.line1.temp (seq 102)
    JS->>JS: Store: factory.line1.temp (seq 103)

    Note over GW2: Gateway restarts

    Device->>+GW2: Reconnect & Authenticate
    GW2-->>-Device: Authenticated

    Device->>GW2: {"type": 1, "subject": "factory.line1.temp"}

    GW2->>+JS: Resume durable consumer "device-sensor-temp-001"
    JS->>JS: Find last ack position (seq 100)
    JS-->>-GW2: Resume from seq 101

    JS->>GW2: Deliver seq 101
    GW2-->>Device: {"type": 3, ...}
    JS->>GW2: Deliver seq 102
    GW2-->>Device: {"type": 3, ...}
    JS->>GW2: Deliver seq 103
    GW2-->>Device: {"type": 3, ...}

    Note over Device,JS: No messages lost!
```

---

## 6. Request/Reply Pattern

### 6.1 Command with Response

```mermaid
sequenceDiagram
    autonumber
    participant HMI as HMI Panel
    participant GW as Gateway
    participant JS as JetStream
    participant Conv as Conveyor

    HMI->>GW: {"type": 0, "subject": "factory.line1.conveyor.cmd", "payload": {"action": "setSpeed", "value": 150}, "correlationId": "req-123"}

    GW->>JS: Publish command
    JS->>GW: Deliver to Conveyor
    GW-->>Conv: {"type": 3, "subject": "factory.line1.conveyor.cmd", "correlationId": "req-123", ...}

    Conv->>Conv: setSpeed(150)

    Conv->>GW: {"type": 0, "subject": "factory.line1.conveyor.response.req-123", "payload": {"success": true, "currentSpeed": 150}}

    GW->>JS: Publish response

    Note over HMI: HMI subscribed to factory.line1.conveyor.response.>

    JS->>GW: Deliver to HMI
    GW-->>HMI: {"type": 3, "subject": "factory.line1.conveyor.response.req-123", "payload": {"success": true, ...}}

    HMI->>HMI: Match correlationId, handle response
```

---

## 7. Complete Production Scenario

### 7.1 Start Line Sequence

```mermaid
sequenceDiagram
    autonumber
    participant HMI as HMI Panel
    participant GW as Gateway
    participant Orch as Orchestrator
    participant Conv as Conveyor
    participant Vision as Vision Scanner
    participant Counter as Counter

    HMI->>GW: Cmd: start_line to orchestrator
    GW-->>Orch: {"action": "start_line"}

    Orch->>Orch: setLineState(STARTING)

    Orch->>GW: Cmd: start to conveyor
    GW-->>Conv: {"action": "start"}
    Conv->>Conv: start()
    Conv->>GW: Status: mode=ramping
    GW-->>Orch: Conveyor ramping

    Note over Conv: Conveyor ramps up to target speed

    Conv->>GW: Status: mode=running, speed=100
    GW-->>Orch: Conveyor running
    GW-->>Vision: Conveyor running (subscribed)
    GW-->>Counter: Conveyor running (subscribed)

    Vision->>Vision: startScanning()
    Counter->>Counter: startCounting()

    Orch->>Orch: setLineState(RUNNING)
    Orch->>GW: Status: lineState=running
    GW-->>HMI: Line running

    Note over HMI,Counter: Production begins
```

### 7.2 Alert Escalation Flow

```mermaid
sequenceDiagram
    autonumber
    participant Temp as Temp Sensor
    participant GW as Gateway
    participant JS as JetStream
    participant Orch as Orchestrator
    participant HMI as HMI Panel

    Temp->>Temp: Read temperature: 76°F

    Note over Temp: Exceeds warning threshold (75°F)

    Temp->>GW: Telemetry: {"value": 76, "status": "warning"}
    GW->>JS: Store in TELEMETRY stream

    Temp->>GW: Alert: {"severity": "warning", "type": "temperature_high", "value": 76}
    GW->>JS: Store in ALERTS stream

    JS->>GW: Deliver to Orch
    GW-->>Orch: Warning alert
    Orch->>Orch: Log alert, start escalation timer

    JS->>GW: Deliver to HMI
    GW-->>HMI: Warning alert
    HMI->>HMI: Display warning indicator

    Note over Temp: Temperature continues rising...

    Temp->>Temp: Read temperature: 81°F

    Note over Temp: Exceeds critical threshold (80°F)

    Temp->>GW: Alert: {"severity": "critical", "type": "temperature_high", "value": 81}
    GW->>JS: Store

    JS->>GW: Deliver to Orch
    GW-->>Orch: Critical alert
    Orch->>Orch: Initiate line slowdown

    Orch->>GW: Cmd: setSpeed(50) to conveyor
    GW-->>GW: Route to conveyor

    JS->>GW: Deliver to HMI
    GW-->>HMI: Critical alert
    HMI->>HMI: Display critical alarm, audible alert
```

---

## 8. Message Flow Summary

```mermaid
sequenceDiagram
    participant D as Devices
    participant SDK as C++ SDK
    participant WS as WebSocket
    participant GW as Gateway
    participant JS as JetStream
    participant NATS as NATS Core

    Note over D,NATS: Publish Path
    D->>SDK: publish(subject, payload)
    SDK->>SDK: Serialize to JSON
    SDK->>WS: WebSocket Text Frame
    WS->>GW: Receive message
    GW->>GW: Validate & Authorize
    GW->>JS: PublishAsync()
    JS->>JS: Persist to stream

    Note over D,NATS: Subscribe Path
    D->>SDK: subscribe(subject, handler)
    SDK->>WS: Subscribe message
    WS->>GW: Receive subscribe
    GW->>JS: CreateConsumer()
    JS-->>GW: Consumer ready

    Note over D,NATS: Delivery Path
    JS->>GW: Push message
    GW->>GW: Route to device connection
    GW->>WS: WebSocket Text Frame
    WS->>SDK: Receive message
    SDK->>SDK: Deserialize JSON
    SDK->>D: Invoke handler(subject, payload)
```
