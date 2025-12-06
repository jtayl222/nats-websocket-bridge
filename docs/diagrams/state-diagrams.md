# State Diagrams

## 1. Connection State Machine (SDK)

```mermaid
stateDiagram-v2
    [*] --> Disconnected

    Disconnected --> Connecting: connect()
    Connecting --> Authenticating: WebSocket connected
    Connecting --> Disconnected: Connection failed

    Authenticating --> Connected: Auth success
    Authenticating --> Disconnected: Auth failed/timeout

    Connected --> Closing: disconnect()
    Connected --> Reconnecting: Connection lost

    Reconnecting --> Connecting: Retry timer expired
    Reconnecting --> Disconnected: Max retries exceeded

    Closing --> Closed: Graceful close
    Closed --> [*]

    note right of Connected
        Heartbeat active
        Subscriptions active
        Can publish/subscribe
    end note

    note right of Reconnecting
        Exponential backoff
        1s, 2s, 4s, 8s... max 30s
        Jitter ±25%
    end note
```

## 2. WebSocket Handler State Machine (Gateway)

```mermaid
stateDiagram-v2
    [*] --> Accepted

    Accepted --> WaitingForAuth: Start auth timer
    WaitingForAuth --> Authenticated: Valid auth message
    WaitingForAuth --> Closed: Auth timeout (30s)
    WaitingForAuth --> Closed: Invalid credentials

    Authenticated --> Processing: Ready for messages

    state Processing {
        [*] --> Idle
        Idle --> ReceivingMessage: Message received
        ReceivingMessage --> Validating: Parse success
        ReceivingMessage --> Idle: Parse error (send error)

        Validating --> Authorizing: Valid message
        Validating --> Idle: Invalid (send error)

        Authorizing --> Executing: Authorized
        Authorizing --> Idle: Not authorized (send error)

        Executing --> Publishing: Publish message
        Executing --> Subscribing: Subscribe message
        Executing --> Unsubscribing: Unsubscribe message
        Executing --> Ponging: Ping message

        Publishing --> Idle: Published
        Subscribing --> Idle: Subscribed (send ack)
        Unsubscribing --> Idle: Unsubscribed
        Ponging --> Idle: Pong sent
    end

    Processing --> Closing: Client disconnect
    Processing --> Closing: Error/timeout

    Closing --> Closed: Cleanup complete
    Closed --> [*]
```

## 3. Conveyor Controller State Machine

```mermaid
stateDiagram-v2
    [*] --> Stopped

    Stopped --> Ramping: start command
    Stopped --> Fault: Fault detected

    Ramping --> Running: Target speed reached
    Ramping --> Stopped: stop command
    Ramping --> EmergencyStop: E-Stop received
    Ramping --> Fault: Fault detected

    Running --> Ramping: setSpeed command
    Running --> Ramping: stop command (target=0)
    Running --> EmergencyStop: E-Stop received
    Running --> Fault: Fault detected

    EmergencyStop --> Stopped: reset command

    Fault --> Stopped: reset command (if clearable)

    note right of Ramping
        Speed changes at 50 units/sec
        Towards target speed
    end note

    note right of EmergencyStop
        Immediate halt
        Requires manual reset
    end note
```

## 4. Line Orchestrator State Machine

```mermaid
stateDiagram-v2
    [*] --> Unknown

    Unknown --> Stopped: All devices online

    state Stopped {
        [*] --> Idle
        Idle --> PreStartCheck: start_line command
        PreStartCheck --> Idle: Check failed
        PreStartCheck --> ReadyToStart: All checks pass
    }

    Stopped --> Starting: start_line command

    state Starting {
        [*] --> StartingConveyor
        StartingConveyor --> WaitingForSpeed: Conveyor ramping
        WaitingForSpeed --> StartingScanner: Speed reached
        StartingScanner --> StartingCounter: Scanner ready
        StartingCounter --> AllStarted: Counter ready
    }

    Starting --> Running: All subsystems started
    Starting --> Fault: Subsystem failed to start

    Running --> Stopping: stop_line command
    Running --> Emergency: E-Stop received
    Running --> Fault: Critical error

    state Stopping {
        [*] --> StoppingConveyor
        StoppingConveyor --> WaitingForStop: Conveyor ramping down
        WaitingForStop --> Stopped: Conveyor stopped
    }

    Emergency --> Stopped: Emergency cleared
    Fault --> Stopped: Fault cleared

    note right of Running
        OEE calculation active
        Alert monitoring
        All devices producing
    end note
```

## 5. Authentication State Machine

```mermaid
stateDiagram-v2
    [*] --> NotAuthenticated

    NotAuthenticated --> Authenticating: startAuth()

    state Authenticating {
        [*] --> SendingRequest
        SendingRequest --> WaitingResponse: Request sent
        WaitingResponse --> ProcessingResponse: Response received
        WaitingResponse --> TimedOut: Timeout
    }

    Authenticating --> Authenticated: Success response
    Authenticating --> Failed: Failure response
    Authenticating --> Failed: Timeout

    Authenticated --> NotAuthenticated: disconnect/reset

    Failed --> NotAuthenticated: reset

    note right of Authenticated
        Device permissions loaded
        Can check canPublish/canSubscribe
    end note
```

## 6. Message Processing Pipeline

```mermaid
stateDiagram-v2
    direction LR

    [*] --> Received

    Received --> Parsed: JSON valid
    Received --> Error: JSON invalid

    Parsed --> TypeChecked: Known type
    Parsed --> Error: Unknown type

    TypeChecked --> SubjectValidated: Subject valid
    TypeChecked --> Processing: No subject needed (Ping/Pong)

    SubjectValidated --> PayloadValidated: Size OK
    SubjectValidated --> Error: Subject invalid

    PayloadValidated --> Authorized: Size < 1MB
    PayloadValidated --> Error: Payload too large

    Authorized --> RateLimited: Permission granted
    Authorized --> Error: Not authorized

    RateLimited --> Processing: Under limit
    RateLimited --> Error: Rate exceeded

    Processing --> Published: Publish type
    Processing --> Subscribed: Subscribe type
    Processing --> Unsubscribed: Unsubscribe type
    Processing --> Ponged: Ping type

    Published --> Complete
    Subscribed --> Complete
    Unsubscribed --> Complete
    Ponged --> Complete

    Error --> Complete: Error sent to client

    Complete --> [*]
```

## 7. Reconnection Backoff State Machine

```mermaid
stateDiagram-v2
    [*] --> Idle

    Idle --> Attempt1: Connection lost
    Attempt1 --> Connecting: Wait 1s

    Connecting --> Connected: Success
    Connecting --> Attempt2: Failed

    Attempt2 --> Connecting: Wait 2s
    Connecting --> Attempt3: Failed

    Attempt3 --> Connecting: Wait 4s
    Connecting --> Attempt4: Failed

    Attempt4 --> Connecting: Wait 8s
    Connecting --> Attempt5: Failed

    Attempt5 --> Connecting: Wait 16s
    Connecting --> AttemptN: Failed

    AttemptN --> Connecting: Wait 30s (max)
    Connecting --> MaxRetriesExceeded: Max attempts

    Connected --> Idle: Reset backoff

    MaxRetriesExceeded --> [*]: Give up

    note right of AttemptN
        Jitter applied: ±25%
        Max delay: 30s
        Max attempts: configurable (0=unlimited)
    end note
```

## 8. Quality Scanner State Machine

```mermaid
stateDiagram-v2
    [*] --> Idle

    Idle --> Scanning: Conveyor running
    Idle --> Idle: Conveyor stopped

    state Scanning {
        [*] --> WaitingForItem
        WaitingForItem --> Capturing: Item detected
        Capturing --> Analyzing: Image captured
        Analyzing --> Deciding: Analysis complete

        state Deciding {
            [*] --> CheckingDefects
            CheckingDefects --> Pass: No defects
            CheckingDefects --> Reject: Defect found
        }

        Pass --> Publishing: Increment good count
        Reject --> Publishing: Increment reject, publish defect

        Publishing --> WaitingForItem: Stats published
    }

    Scanning --> Idle: Conveyor stopped
    Scanning --> Emergency: E-Stop received

    Emergency --> Idle: Emergency cleared

    note right of Analyzing
        Defect types:
        - label_misalignment
        - missing_label
        - damaged_package
        - contamination
        - barcode_unreadable
    end note
```

## 9. E-Stop Button State Machine

```mermaid
stateDiagram-v2
    [*] --> Ready

    Ready --> Triggered: Button pressed
    Ready --> Ready: Status request

    state Triggered {
        [*] --> BroadcastingEmergency
        BroadcastingEmergency --> AwaitingReset: Emergency broadcast sent
    }

    Triggered --> Ready: Reset command

    note right of Triggered
        Latching behavior
        All devices receive emergency
        Manual reset required
    end note

    note left of Ready
        Green indicator
        System normal
    end note
```

## 10. OEE Calculation State

```mermaid
stateDiagram-v2
    [*] --> Accumulating

    state Accumulating {
        [*] --> CollectingData

        state CollectingData {
            UpdateRuntime: Increment actual runtime
            UpdateDowntime: Increment downtime
            UpdateGoodCount: Increment good count
            UpdateTotalCount: Increment total count
        }

        CollectingData --> Calculating: Publish interval

        state Calculating {
            CalcAvailability: (Planned - Downtime) / Planned
            CalcPerformance: (IdealCycle × Total) / Runtime
            CalcQuality: Good / Total
            CalcOEE: Availability × Performance × Quality
        }

        Calculating --> Publishing: Calculations done
        Publishing --> CollectingData: Published
    }

    Accumulating --> Reset: reset command
    Reset --> Accumulating: Counters cleared

    note right of Calculating
        World-class OEE: 85%+
        Availability: target 90%+
        Performance: target 95%+
        Quality: target 99.9%+
    end note
```
