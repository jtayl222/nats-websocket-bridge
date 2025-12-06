# Presentation Diagrams

Key diagrams optimized for presentations and documentation.

## 1. System Overview (High-Level)

```mermaid
graph TB
    subgraph "Factory Floor Devices"
        D1[🌡️ Temperature<br/>Sensors]
        D2[⚙️ Conveyor<br/>Controllers]
        D3[📷 Vision<br/>Scanners]
        D4[🛑 E-Stop<br/>Buttons]
        D5[📊 Production<br/>Counters]
    end

    subgraph "Gateway Layer"
        GW[🔌 WebSocket Gateway<br/>Authentication | Authorization<br/>Validation | Rate Limiting]
    end

    subgraph "Messaging Layer"
        NATS[📬 NATS + JetStream<br/>Persistence | Replay<br/>Fan-out | Durability]
    end

    subgraph "Enterprise Systems"
        E1[📺 HMI/SCADA]
        E2[🏭 MES]
        E3[📈 Historian]
    end

    D1 & D2 & D3 & D4 & D5 -->|WebSocket + TLS| GW
    GW <-->|NATS Protocol| NATS
    NATS <--> E1 & E2 & E3

    style GW fill:#4CAF50,color:white
    style NATS fill:#2196F3,color:white
```

## 2. Connection Flow (Simplified)

```mermaid
sequenceDiagram
    participant Device
    participant Gateway
    participant NATS

    Device->>Gateway: 1. WebSocket Connect
    Gateway-->>Device: 2. Connection Accepted

    Device->>Gateway: 3. Authenticate {deviceId, token}
    Gateway-->>Device: 4. Auth OK + Permissions

    Device->>Gateway: 5. Subscribe to commands
    Gateway->>NATS: 6. Create consumer

    Device->>Gateway: 7. Publish telemetry
    Gateway->>NATS: 8. Store in JetStream

    NATS->>Gateway: 9. Deliver command
    Gateway-->>Device: 10. Forward to device
```

## 3. Emergency Stop Broadcast

```mermaid
sequenceDiagram
    participant EStop as 🛑 E-Stop
    participant GW as Gateway
    participant Conv as ⚙️ Conveyor
    participant Vision as 📷 Scanner
    participant Counter as 📊 Counter

    Note over EStop: Operator presses E-Stop

    EStop->>GW: Emergency Stop!

    par Broadcast to all devices
        GW-->>Conv: 🛑 STOP
        Conv->>Conv: Halt motor
    and
        GW-->>Vision: 🛑 STOP
        Vision->>Vision: Stop scanning
    and
        GW-->>Counter: 🛑 STOP
        Counter->>Counter: Pause counting
    end

    Note over Conv,Counter: All devices halted < 100ms
```

## 4. Reconnection with Replay

```mermaid
sequenceDiagram
    participant Device
    participant Gateway
    participant JetStream

    Note over Device: Connection lost

    Device--xGateway: ❌ Disconnected

    Note over JetStream: Messages continue storing

    JetStream->>JetStream: Store msg 101
    JetStream->>JetStream: Store msg 102
    JetStream->>JetStream: Store msg 103

    Note over Device: Auto-reconnect (backoff)

    Device->>Gateway: Reconnect
    Gateway-->>Device: ✓ Authenticated

    Device->>Gateway: Resubscribe

    Note over JetStream: Replay missed messages

    JetStream->>Gateway: Deliver 101
    Gateway-->>Device: Message 101

    JetStream->>Gateway: Deliver 102
    Gateway-->>Device: Message 102

    JetStream->>Gateway: Deliver 103
    Gateway-->>Device: Message 103

    Note over Device,JetStream: No messages lost!
```

## 5. SDK Usage (Code-Like Sequence)

```mermaid
sequenceDiagram
    participant App as Application
    participant SDK as C++ SDK
    participant WS as WebSocket
    participant GW as Gateway

    App->>SDK: GatewayClient(config)
    App->>SDK: client.connect()
    SDK->>WS: WebSocket CONNECT
    WS->>GW: Handshake
    SDK->>GW: Authenticate
    GW-->>SDK: Auth OK
    SDK-->>App: return true

    App->>SDK: client.subscribe("cmd.>", handler)
    SDK->>GW: Subscribe message
    GW-->>SDK: ACK

    loop Every 5 seconds
        App->>SDK: client.publish("temp", {value: 72.3})
        SDK->>GW: Publish message
    end

    loop Main loop
        App->>SDK: client.poll()
        Note over SDK: Process incoming messages
        SDK->>App: handler(subject, payload)
    end
```

## 6. Architecture Layers

```mermaid
graph TB
    subgraph "Layer 4: Application"
        A1[Device Firmware]
        A2[Control Logic]
    end

    subgraph "Layer 3: SDK"
        S1[GatewayClient API]
        S2[Connection Mgmt]
        S3[Protocol Handler]
    end

    subgraph "Layer 2: Transport"
        T1[WebSocket]
        T2[TLS Encryption]
    end

    subgraph "Layer 1: Gateway"
        G1[Auth/AuthZ]
        G2[Validation]
        G3[Routing]
    end

    subgraph "Layer 0: Messaging"
        M1[NATS Core]
        M2[JetStream]
    end

    A1 & A2 --> S1
    S1 --> S2 --> S3
    S3 --> T1 --> T2
    T2 --> G1 --> G2 --> G3
    G3 --> M1 <--> M2
```

## 7. Data Flow Diagram

```mermaid
flowchart LR
    subgraph Sources
        S1((Temp<br/>Sensor))
        S2((Conveyor))
        S3((Vision))
    end

    subgraph Gateway
        direction TB
        V[Validate]
        A[Authorize]
        R[Route]
    end

    subgraph Streams
        T[(TELEMETRY)]
        E[(EVENTS)]
        L[(ALERTS)]
    end

    subgraph Consumers
        C1[HMI]
        C2[Historian]
        C3[Alerts]
    end

    S1 -->|temp data| V
    S2 -->|status| V
    S3 -->|quality| V

    V --> A --> R

    R -->|temp.*| T
    R -->|status.*| E
    R -->|alerts.*| L

    T --> C1 & C2
    E --> C1 & C2
    L --> C1 & C3
```

## 8. OEE Dashboard

```mermaid
graph TB
    subgraph "OEE = Availability × Performance × Quality"
        subgraph "Availability"
            A1[Planned Time: 8h]
            A2[Downtime: 30min]
            A3[= 93.75%]
        end

        subgraph "Performance"
            P1[Ideal Rate: 100/min]
            P2[Actual: 92/min]
            P3[= 92%]
        end

        subgraph "Quality"
            Q1[Total: 44,160]
            Q2[Good: 43,500]
            Q3[= 98.5%]
        end

        OEE[OEE = 84.9%]
    end

    A3 --> OEE
    P3 --> OEE
    Q3 --> OEE

    style OEE fill:#4CAF50,color:white,font-size:20px
```

## 9. Security Model

```mermaid
flowchart TB
    subgraph "Device"
        D[Device with Token]
    end

    subgraph "Transport Security"
        TLS[TLS 1.2+<br/>Encrypted Channel]
    end

    subgraph "Gateway Security"
        AUTH[Authentication<br/>Token Validation]
        AUTHZ[Authorization<br/>Topic Permissions]
        RATE[Rate Limiting<br/>100 msg/sec]
        VAL[Validation<br/>Size, Format]
    end

    subgraph "Protected Resources"
        N[NATS/JetStream]
    end

    D -->|1. Connect| TLS
    TLS -->|2. Authenticate| AUTH
    AUTH -->|3. Check Permissions| AUTHZ
    AUTHZ -->|4. Rate Check| RATE
    RATE -->|5. Validate| VAL
    VAL -->|6. Forward| N

    style TLS fill:#E91E63,color:white
    style AUTH fill:#9C27B0,color:white
    style AUTHZ fill:#673AB7,color:white
```

## 10. Demo Scenario Timeline

```mermaid
gantt
    title Packaging Line Demo Scenario
    dateFormat HH:mm
    axisFormat %H:%M

    section Setup
    Start Gateway           :a1, 00:00, 1m
    Start Devices           :a2, after a1, 1m
    All Connected           :milestone, after a2, 0m

    section Production
    Start Line              :b1, after a2, 1m
    Normal Operation        :b2, after b1, 3m

    section Demo 1: Telemetry
    Show Temp Readings      :c1, after b2, 2m
    Show Quality Stats      :c2, after c1, 2m

    section Demo 2: Alerts
    Inject Temp Spike       :d1, after c2, 1m
    Alert Triggers          :milestone, after d1, 0m
    Return to Normal        :d2, after d1, 2m

    section Demo 3: Emergency
    Trigger E-Stop          :e1, after d2, 30s
    All Devices Stop        :milestone, after e1, 0m
    Reset E-Stop            :e2, after e1, 1m

    section Demo 4: Resilience
    Kill Conveyor           :f1, after e2, 30s
    Reconnect               :f2, after f1, 1m
    State Replayed          :milestone, after f2, 0m

    section Wrap Up
    Show OEE Stats          :g1, after f2, 2m
    Stop Line               :g2, after g1, 1m
```
