# Component Diagrams

## 1. System Architecture Overview

```mermaid
graph TB
    subgraph "Factory Floor"
        subgraph "Packaging Line 1"
            TS[Temperature Sensor]
            CC[Conveyor Controller]
            VS[Vision Scanner]
            ES[E-Stop Button]
            PC[Production Counter]
        end

        subgraph "Control Room"
            HMI[HMI Panel]
            ORCH[Line Orchestrator]
        end
    end

    subgraph "Edge Gateway"
        GW[WebSocket Gateway]
        AUTH[Auth Service]
        AUTHZ[Authorization Service]
        VAL[Validation Service]
        THROT[Throttling Service]
        CONN[Connection Manager]
        BUF[Message Buffer]
    end

    subgraph "Messaging Layer"
        NATS[NATS Server]
        JS[JetStream]
        subgraph "Streams"
            S1[TELEMETRY]
            S2[EVENTS]
            S3[ALERTS]
        end
    end

    subgraph "Enterprise Systems"
        SCADA[SCADA]
        MES[MES]
        HIST[Historian]
    end

    TS -->|WebSocket/TLS| GW
    CC -->|WebSocket/TLS| GW
    VS -->|WebSocket/TLS| GW
    ES -->|WebSocket/TLS| GW
    PC -->|WebSocket/TLS| GW
    HMI -->|WebSocket/TLS| GW
    ORCH -->|WebSocket/TLS| GW

    GW --> AUTH
    GW --> AUTHZ
    GW --> VAL
    GW --> THROT
    GW --> CONN
    GW --> BUF

    GW <-->|NATS Protocol| NATS
    NATS <--> JS
    JS --> S1
    JS --> S2
    JS --> S3

    NATS <-->|NATS| SCADA
    NATS <-->|NATS| MES
    JS -->|Replay| HIST
```

## 2. Gateway Internal Components

```mermaid
graph TB
    subgraph "WebSocket Layer"
        MW[WebSocket Middleware]
        WSH[WebSocket Handler]
    end

    subgraph "Security Layer"
        AUTH[IDeviceAuthenticationService]
        AUTHZ[IDeviceAuthorizationService]
    end

    subgraph "Processing Layer"
        VAL[IMessageValidationService]
        THROT[IMessageThrottlingService]
        BUF[IMessageBufferService]
    end

    subgraph "Connection Layer"
        CONN[IDeviceConnectionManager]
    end

    subgraph "NATS Layer"
        JSVC[IJetStreamNatsService]
    end

    subgraph "Configuration"
        OPTS[GatewayOptions]
        NOPTS[NatsOptions]
        JSOPTS[JetStreamOptions]
    end

    MW -->|HTTP Upgrade| WSH
    WSH --> AUTH
    WSH --> AUTHZ
    WSH --> VAL
    WSH --> THROT
    WSH --> BUF
    WSH --> CONN
    WSH --> JSVC

    JSVC --> NOPTS
    JSVC --> JSOPTS
    WSH --> OPTS

    style AUTH fill:#f9f,stroke:#333
    style AUTHZ fill:#f9f,stroke:#333
    style VAL fill:#bbf,stroke:#333
    style THROT fill:#bbf,stroke:#333
    style BUF fill:#bbf,stroke:#333
    style JSVC fill:#bfb,stroke:#333
```

## 3. C++ SDK Components

```mermaid
graph TB
    subgraph "Public API"
        GC[GatewayClient]
        CFG[GatewayConfig]
        MSG[Message / JsonValue]
    end

    subgraph "Core Components"
        TRANS[ITransport]
        WSTRANS[WebSocketTransport]
        PROTO[Protocol]
        AUTHMGR[AuthManager]
    end

    subgraph "Support Components"
        RECONN[ReconnectPolicy]
        LOG[Logger]
        ERR[Error Handling]
    end

    subgraph "External Dependencies"
        LWS[libwebsockets]
        JSON[nlohmann/json]
        SSL[OpenSSL]
    end

    GC --> CFG
    GC --> MSG
    GC --> TRANS
    GC --> AUTHMGR
    GC --> RECONN
    GC --> LOG

    TRANS --> WSTRANS
    WSTRANS --> PROTO
    PROTO --> MSG

    WSTRANS --> LWS
    PROTO --> JSON
    LWS --> SSL

    style GC fill:#bfb,stroke:#333
    style CFG fill:#bfb,stroke:#333
    style MSG fill:#bfb,stroke:#333
```

## 4. Message Flow Component View

```mermaid
graph LR
    subgraph "Device"
        APP[Application]
        SDK[SDK Client]
    end

    subgraph "Transport"
        WS[WebSocket]
        TLS[TLS]
    end

    subgraph "Gateway"
        RECV[Receiver]
        PROC[Processor]
        SEND[Sender]
    end

    subgraph "NATS"
        PUB[Publisher]
        SUB[Subscriber]
        STR[Stream]
    end

    APP -->|publish()| SDK
    SDK -->|JSON| WS
    WS -->|Encrypted| TLS
    TLS -->|TCP| RECV

    RECV -->|Parse| PROC
    PROC -->|Validate| PROC
    PROC -->|Authorize| PROC
    PROC -->|Publish| PUB

    PUB --> STR
    STR --> SUB
    SUB --> SEND

    SEND -->|Route| WS
    WS --> SDK
    SDK -->|callback| APP
```

## 5. JetStream Streams Configuration

```mermaid
graph TB
    subgraph "JetStream"
        subgraph "TELEMETRY Stream"
            T1[Subject: factory.*.temp]
            T2[Subject: factory.*.humidity]
            T3[Subject: factory.*.pressure]
            TMEM[Memory Storage]
            TRET[Retention: 24h]
        end

        subgraph "EVENTS Stream"
            E1[Subject: factory.*.status.*]
            E2[Subject: factory.*.output]
            E3[Subject: factory.*.quality.*]
            EFILE[File Storage]
            ERET[Retention: 7 days]
        end

        subgraph "ALERTS Stream"
            A1[Subject: factory.*.alerts.*]
            A2[Subject: factory.*.emergency]
            A3[Subject: factory.*.eStop]
            AFILE[File Storage]
            ARET[Retention: 30 days]
            APROT[DenyDelete, DenyPurge]
        end
    end

    subgraph "Consumers"
        C1[Device Consumers<br/>Durable, Push]
        C2[Historian Consumer<br/>Durable, Pull]
        C3[Alert Consumer<br/>Durable, Push]
    end

    T1 --> TMEM
    T2 --> TMEM
    T3 --> TMEM
    TMEM --> TRET

    E1 --> EFILE
    E2 --> EFILE
    E3 --> EFILE
    EFILE --> ERET

    A1 --> AFILE
    A2 --> AFILE
    A3 --> AFILE
    AFILE --> ARET
    ARET --> APROT

    TELEMETRY --> C1
    EVENTS --> C2
    ALERTS --> C3
```

## 6. Device Type Hierarchy

```mermaid
graph TB
    subgraph "Device Types"
        BASE[Device Base]

        subgraph "Sensors"
            SENS[Sensor]
            TEMP[Temperature]
            VISION[Vision]
            ESTOP[E-Stop]
            COUNT[Counter]
        end

        subgraph "Actuators"
            ACT[Actuator]
            CONV[Conveyor]
            VALVE[Valve]
            MOTOR[Motor]
        end

        subgraph "Controllers"
            CTRL[Controller]
            PLC[PLC/Orchestrator]
            HMI[HMI Panel]
        end
    end

    BASE --> SENS
    BASE --> ACT
    BASE --> CTRL

    SENS --> TEMP
    SENS --> VISION
    SENS --> ESTOP
    SENS --> COUNT

    ACT --> CONV
    ACT --> VALVE
    ACT --> MOTOR

    CTRL --> PLC
    CTRL --> HMI
```

## 7. Authentication & Authorization Flow

```mermaid
graph TB
    subgraph "Authentication"
        REQ[Auth Request]
        LOOKUP[Device Lookup]
        VERIFY[Token Verify]
        LOAD[Load Permissions]
        RESP[Auth Response]
    end

    subgraph "Authorization"
        PUB[Publish Check]
        SUB[Subscribe Check]
        MATCH[Pattern Match]
    end

    subgraph "Patterns"
        WILD1["* = single token"]
        WILD2["> = multi token"]
        EXACT[Exact match]
    end

    REQ --> LOOKUP
    LOOKUP -->|Found| VERIFY
    LOOKUP -->|Not Found| FAIL[Fail]
    VERIFY -->|Valid| LOAD
    VERIFY -->|Invalid| FAIL
    LOAD --> RESP

    RESP --> PUB
    RESP --> SUB

    PUB --> MATCH
    SUB --> MATCH

    MATCH --> WILD1
    MATCH --> WILD2
    MATCH --> EXACT
```

## 8. Deployment View

```mermaid
graph TB
    subgraph "Production Environment"
        subgraph "DMZ"
            LB[Load Balancer]
        end

        subgraph "Gateway Cluster"
            GW1[Gateway 1]
            GW2[Gateway 2]
            GW3[Gateway 3]
        end

        subgraph "NATS Cluster"
            N1[NATS 1]
            N2[NATS 2]
            N3[NATS 3]
        end

        subgraph "Storage"
            S1[(JetStream Storage)]
        end
    end

    subgraph "Factory Network"
        DEV[Devices]
    end

    DEV -->|WSS| LB
    LB --> GW1
    LB --> GW2
    LB --> GW3

    GW1 <--> N1
    GW2 <--> N2
    GW3 <--> N3

    N1 <--> N2
    N2 <--> N3
    N3 <--> N1

    N1 --> S1
    N2 --> S1
    N3 --> S1
```
