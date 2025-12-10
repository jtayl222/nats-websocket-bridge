# Slide Deck Outline

Slide-by-slide breakdown for the YouTube video presentation.

---

## Section 1: HOOK (0:00 - 1:00)

### Slide 1: Title Slide
**Building Real-Time Device Gateways with NATS and WebSockets**

- Your Name / Title
- Date
- Company/Channel Logo

---

### Slide 2: The Problem
**Visual: Chaotic diagram showing:**

```
┌──────────────────────────────────────────────────────────────────┐
│                    THE REALITY                                    │
│                                                                  │
│   [IoT Device] ──HTTP──┐                                         │
│   [Mobile App] ──WS────┤     ┌─────────┐     ┌──────────┐       │
│   [Sensor]    ──MQTT───┼────►│ ?????? │────►│ Backend  │       │
│   [PLC]       ──TCP────┤     │ Chaos!  │     │ Services │       │
│   [Gateway]   ──REST───┘     └─────────┘     └──────────┘       │
│                                                                  │
│   • Different protocols       • Unreliable networks              │
│   • No standard approach      • Guaranteed delivery needed       │
│   • Security concerns         • Scale to thousands               │
└──────────────────────────────────────────────────────────────────┘
```

**Talking Points:**
- Hundreds of devices speaking different protocols
- Unreliable networks, disconnections
- Need guaranteed delivery
- Enterprise systems expect reliable data

---

## Section 2: THESIS (1:00 - 2:00)

### Slide 3: The Three-Layer Solution

**Visual: Clean architecture overview**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────────┐   │
│  │   Device    │     │   Protocol  │     │     NATS +      │   │
│  │    SDK      │────►│   Gateway   │────►│   JetStream     │   │
│  │             │     │             │     │                 │   │
│  │ • Consistent│     │ • Security  │     │ • Persistence   │   │
│  │ • Reliable  │     │ • Translate │     │ • Fan-out       │   │
│  │ • Simple    │     │ • Validate  │     │ • Replay        │   │
│  └─────────────┘     └─────────────┘     └─────────────────┘   │
│                                                                 │
│       LAYER 1              LAYER 2             LAYER 3          │
└─────────────────────────────────────────────────────────────────┘
```

**Talking Points:**
- Three layers, each with distinct responsibility
- SDK: What device vendors use
- Gateway: Your security and translation boundary
- NATS: The reliable messaging backbone

---

### Slide 4: What We'll Cover

**Agenda:**

1. Why NATS? (Choosing the messaging layer)
2. The Gateway Pattern (Why not connect directly?)
3. The SDK Pattern (Standardizing device integration)
4. Live Demo (See it work)
5. Patterns to Apply Today

---

## Section 3: WHY NATS? (2:00 - 5:00)

### Slide 5: Messaging Options

**Comparison Table:**

| Feature | NATS | Kafka | RabbitMQ | MQTT |
|---------|------|-------|----------|------|
| **Latency** | μs | ms | ms | ms |
| **Ops Complexity** | Low | High | Medium | Low |
| **Persistence** | JetStream | Built-in | Plugin | Broker |
| **Horizontal Scale** | Excellent | Excellent | Good | Good |
| **Protocol** | Simple | Complex | AMQP | MQTT |

**Key Insight:** Each has trade-offs. NATS optimizes for simplicity + performance.

---

### Slide 6: NATS Differentiators

**Visual: Three columns**

```
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│   SIMPLICITY    │  │   PERFORMANCE   │  │   OPERATIONS    │
├─────────────────┤  ├─────────────────┤  ├─────────────────┤
│                 │  │                 │  │                 │
│ • Single binary │  │ • μs latency    │  │ • Zero config   │
│ • Zero config   │  │ • 18M msg/sec   │  │ • Self-healing  │
│ • Text protocol │  │ • 8 bytes       │  │ • No ZooKeeper  │
│                 │  │   overhead      │  │ • Just add nodes│
└─────────────────┘  └─────────────────┘  └─────────────────┘
```

**Talking Points:**
- Single binary, downloads in seconds
- Microsecond latency for core NATS
- No broker affinity - true horizontal scaling
- No coordination service needed (unlike Kafka)

---

### Slide 7: JetStream - Best of Both Worlds

**Visual: Spectrum diagram**

```
         SPEED                                    DURABILITY
           │                                           │
           ▼                                           ▼
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│   Core NATS          JetStream           JetStream +         │
│   (Fire & Forget)    (Persistence)       Replicated          │
│                                                              │
│   • Fastest          • At-least-once     • Exactly-once      │
│   • No guarantees    • Replay            • HA/DR             │
│   • Ephemeral        • Retention         • Audit trail       │
│                                                              │
└──────────────────────────────────────────────────────────────┘
                           ▲
                           │
              Choose based on your needs
```

**Talking Points:**
- JetStream adds persistence *when you need it*
- Same API, same infrastructure
- Stream retention policies (time, size, count)
- Consumer replay from any point

---

### Slide 8: Subject-Based Routing

**Visual: Subject hierarchy example**

```
factory.line1.conveyor.status     →  Conveyor status updates
factory.line1.conveyor.cmd        →  Commands to conveyor
factory.line1.*.status            →  All device statuses on line 1
factory.>                         →  Everything in factory
factory.line1.alerts.>            →  All alerts on line 1

Wildcard Patterns:
  *  = matches single token     factory.*.status
  >  = matches multiple tokens  factory.line1.>
```

**Talking Points:**
- Hierarchical subjects enable powerful routing
- Wildcards for flexible subscriptions
- Authorization per subject pattern
- Natural mapping to device/entity hierarchy

---

### Slide 9: NATS Cluster

**Visual: Three-node cluster diagram**

```
                    ┌──────────┐
                    │  NATS 1  │
                    │ (Leader) │
                    └────┬─────┘
                         │
            ┌────────────┼────────────┐
            │            │            │
       ┌────▼────┐  ┌────▼────┐  ┌────▼────┐
       │ Client  │  │ Client  │  │ Client  │
       └────┬────┘  └────┬────┘  └────┬────┘
            │            │            │
       ┌────▼────┐  ┌────▼────┐       │
       │  NATS 2 │◄─►  NATS 3 │◄──────┘
       │         │  │         │
       └─────────┘  └─────────┘

✓ No single point of failure
✓ Clients auto-failover
✓ JetStream replication
```

**Talking Points:**
- Clients connect to any node
- Automatic failover
- JetStream streams replicated across nodes
- No data loss on node failure

---

## Section 4: THE GATEWAY PATTERN (5:00 - 9:00)

### Slide 10: Why Not Connect Directly?

**Visual: X over direct connection**

```
        ❌ DON'T DO THIS

   [Device] ────────────────────► [NATS]
            (Direct connection)

   Problems:
   • Devices on untrusted networks
   • NATS credentials on every device
   • No protocol translation
   • No rate limiting
   • No message validation
   • Impossible to rotate credentials
```

**Talking Points:**
- Devices are on untrusted networks
- NATS credentials would be on every device
- No opportunity for validation or rate limiting
- Credential rotation nightmare

---

### Slide 11: Gateway as Security Boundary

**Visual: Gateway between zones**

```
┌─────────────────────┐          ┌─────────────────────┐
│   UNTRUSTED ZONE    │          │   TRUSTED ZONE      │
│                     │          │                     │
│  ┌─────────┐        │          │        ┌─────────┐  │
│  │ Device  │────────┼──►[GW]───┼───────►│  NATS   │  │
│  └─────────┘        │          │        └─────────┘  │
│                     │          │                     │
│  • Internet         │   ▲      │  • Internal network │
│  • Factory floor    │   │      │  • Secured          │
│  • Mobile networks  │   │      │  • Monitored        │
│                     │          │                     │
└─────────────────────┘  │       └─────────────────────┘
                         │
               SECURITY BOUNDARY
```

**Talking Points:**
- Gateway is the enforcement point
- Devices never see NATS directly
- All traffic through gateway is validated
- Defense in depth

---

### Slide 12: Gateway Responsibilities

**Visual: Gateway service breakdown**

```
┌─────────────────────────────────────────────────────────┐
│                    GATEWAY LAYER                         │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │   TLS       │  │  AUTHENT-   │  │  AUTHOR-    │     │
│  │ Termination │  │  ICATION    │  │  IZATION    │     │
│  │             │  │             │  │             │     │
│  │ • Encrypt   │  │ • Token     │  │ • Per-topic │     │
│  │ • Certs     │  │ • Device ID │  │ • Wildcards │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  MESSAGE    │  │   RATE      │  │ CONNECTION  │     │
│  │ VALIDATION  │  │  LIMITING   │  │  MGMT       │     │
│  │             │  │             │  │             │     │
│  │ • Size      │  │ • Token     │  │ • Track     │     │
│  │ • Format    │  │   bucket    │  │ • Heartbeat │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

**Talking Points:**
- TLS offloading at gateway
- Authentication validates device identity
- Authorization checks topic permissions
- Rate limiting prevents abuse
- Connection management tracks state

---

### Slide 13: Authentication Flow

**Visual: Sequence diagram (simplified)**

```
   Device                    Gateway                    NATS
     │                          │                         │
     │──── WebSocket Connect ──►│                         │
     │◄─── Connection OK ───────│                         │
     │                          │                         │
     │──── Auth Request ───────►│                         │
     │     {deviceId, token}    │                         │
     │                          │──── Validate ──►        │
     │                          │◄─── OK ─────────        │
     │◄─── Auth OK + Perms ─────│                         │
     │     {pub:[...], sub:[...]}                         │
     │                          │                         │
     │         READY TO COMMUNICATE                       │
```

**Talking Points:**
- WebSocket connects first (could be load balanced)
- Authentication is second message
- Permissions returned to device
- Device knows what it can/cannot do

---

### Slide 14: Authorization in Action

**Visual: Permission examples**

```
Device: "conveyor-001"

Can Publish:
  ✓ factory.line1.conveyor.status
  ✓ factory.line1.alerts.*
  ✗ factory.line2.*          (wrong line)
  ✗ factory.*.cmd            (can't send commands)

Can Subscribe:
  ✓ factory.line1.conveyor.cmd
  ✓ factory.line1.emergency
  ✗ factory.*.status         (too broad)

Pattern matching:
  "factory.*.alerts.*" matches "factory.line1.alerts.temp"
```

**Talking Points:**
- Fine-grained topic permissions
- Wildcard patterns for flexibility
- Separate publish/subscribe permissions
- Principle of least privilege

---

### Slide 15: Code Sample - Gateway Config

**Visual: C# code snippet**

```csharp
// Gateway startup configuration - JWT-based authentication

// Configure JWT options
services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

// Register JWT authentication service
services.AddSingleton<IJwtDeviceAuthService, JwtDeviceAuthService>();

// JWT token contains device permissions:
// - sub: device client ID
// - role: device role (sensor, actuator, admin)
// - pub: ["factory.line1.temp"] - allowed publish topics
// - subscribe: ["factory.line1.cmd.*"] - allowed subscribe topics
```

**Talking Points:**
- JWT-based authentication with embedded permissions
- Permissions encoded in token claims
- Topic patterns support wildcards (* and >)
- Token expiration enforced automatically

---

## Section 5: THE SDK PATTERN (9:00 - 12:00)

### Slide 16: The Integration Challenge

**Visual: Without SDK vs With SDK**

```
WITHOUT SDK:                          WITH SDK:
─────────────────────────────────────────────────────────────
Vendor A:                             All vendors:
  - Custom WebSocket impl
  - Custom JSON parsing               GatewayClient client(cfg);
  - Custom reconnection               client.connect();
  - Custom auth handling              client.publish(...);
  - Bugs, inconsistencies
                                      3 lines of code.
Vendor B:                             Same behavior.
  - Different implementation          Same reliability.
  - Different bugs
  - Different reconnection

Result: Support nightmare             Result: Predictable
```

**Talking Points:**
- Without SDK: every vendor reinvents the wheel
- Different implementations, different bugs
- With SDK: one blessed implementation
- Vendors focus on their domain, not messaging

---

### Slide 17: What the SDK Hides

**Visual: Iceberg diagram**

```
                    VENDOR SEES
              ┌─────────────────────┐
              │  client.connect()   │
              │  client.publish()   │
              │  client.subscribe() │
              └─────────────────────┘
─────────────────────────────────────────────
              │                     │
              │  HIDDEN COMPLEXITY  │
              │                     │
              │  • WebSocket mgmt   │
              │  • TLS negotiation  │
              │  • Auth handshake   │
              │  • Reconnection     │
              │    with backoff     │
              │  • Heartbeats       │
              │  • Message framing  │
              │  • JSON serializing │
              │  • Thread safety    │
              │  • Buffer mgmt      │
              │  • Error handling   │
              └─────────────────────┘
```

**Talking Points:**
- Simple API surface
- Massive complexity hidden
- Tested once, used everywhere
- Consistent behavior across all devices

---

### Slide 18: SDK Design Principles

**Visual: Four principles**

```
┌─────────────────┐  ┌─────────────────┐
│   SIMPLE API    │  │   RELIABILITY   │
├─────────────────┤  ├─────────────────┤
│ • 3 main methods│  │ • Auto-reconnect│
│ • Clear types   │  │ • Exp. backoff  │
│ • Minimal config│  │ • Message buffer│
└─────────────────┘  └─────────────────┘

┌─────────────────┐  ┌─────────────────┐
│   OBSERVABLE    │  │   PORTABLE      │
├─────────────────┤  ├─────────────────┤
│ • State changes │  │ • C++17         │
│ • Error events  │  │ • Linux/Windows │
│ • Statistics    │  │ • ARM/x86       │
└─────────────────┘  └─────────────────┘
```

**Talking Points:**
- Simple: Minimize learning curve
- Reliable: Handle failures gracefully
- Observable: Know what's happening
- Portable: Run on target hardware

---

### Slide 19: Reconnection Strategy

**Visual: Exponential backoff diagram**

```
Connection lost
     │
     ▼
   Retry 1 ─── Wait 1s ──► Connect
     │                        │
     │ failed                 │ success
     ▼                        ▼
   Retry 2 ─── Wait 2s      CONNECTED
     │                        │
     │ failed                 │
     ▼                        │
   Retry 3 ─── Wait 4s       Reset
     │                       backoff
     │ failed                 │
     ▼                        │
   Retry N ─── Wait 30s (max) │
     │                        │
     ▼                        │
   Give up OR keep trying ◄───┘

Jitter: ±25% to prevent thundering herd
```

**Talking Points:**
- Exponential backoff prevents server overload
- Maximum delay caps wait time
- Jitter prevents synchronized retries
- Configurable max attempts (0 = unlimited)

---

### Slide 20: SDK Code Sample

**Visual: C++ code**

```cpp
// Complete device integration

#include <gateway/gateway_device.h>

int main() {
    GatewayConfig config;
    config.gatewayUrl = "wss://gateway.example.com/ws";
    config.deviceId = "temp-sensor-001";
    config.authToken = "device-token";

    GatewayClient client(config);

    // Callbacks for events
    client.onConnected([]() {
        std::cout << "Connected!" << std::endl;
    });

    // Connect (handles auth automatically)
    if (!client.connect()) {
        return 1;
    }

    // Publish telemetry
    while (running) {
        client.publish("factory.line1.temp", {
            {"value", readSensor()},
            {"unit", "celsius"}
        });
        client.poll();
        sleep(5);
    }
}
```

**Talking Points:**
- Configuration is minimal
- Connection includes authentication
- Publish is single line
- Poll processes incoming messages
- No WebSocket/JSON code visible

---

### Slide 21: SDK Architecture

**Visual: Component diagram**

```
┌─────────────────────────────────────────────────────┐
│                   GatewayClient                      │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌────────────┐  │
│  │  Transport  │  │    Auth     │  │ Reconnect  │  │
│  │   Layer     │  │   Manager   │  │   Policy   │  │
│  └──────┬──────┘  └──────┬──────┘  └─────┬──────┘  │
│         │                │               │         │
│  ┌──────▼──────┐  ┌──────▼──────┐  ┌─────▼──────┐  │
│  │  WebSocket  │  │  Protocol   │  │   Logger   │  │
│  │  (libws)    │  │   (JSON)    │  │            │  │
│  └─────────────┘  └─────────────┘  └────────────┘  │
│                                                     │
└─────────────────────────────────────────────────────┘
```

**Talking Points:**
- Modular internal design
- Transport abstraction (could swap WebSocket impl)
- Protocol handles serialization
- Auth manager tracks permissions
- Reconnect policy configurable

---

## Section 6: LIVE DEMO (12:00 - 17:00)

### Slide 22: Demo Setup

**Visual: Demo architecture**

```
┌─────────────────────────────────────────────────────────────┐
│                     DEMO: PACKAGING LINE                     │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│   │  Temp    │  │ Conveyor │  │  Vision  │  │  E-Stop  │   │
│   │ Sensor   │  │Controller│  │ Scanner  │  │  Button  │   │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘   │
│        │             │             │             │         │
│        └─────────────┼─────────────┼─────────────┘         │
│                      │             │                       │
│                      ▼             ▼                       │
│               ┌──────────────────────┐                     │
│               │       GATEWAY        │                     │
│               └──────────┬───────────┘                     │
│                          │                                 │
│                          ▼                                 │
│               ┌──────────────────────┐                     │
│               │    NATS + JetStream  │                     │
│               └──────────────────────┘                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Talking Points:**
- Simulated packaging line
- Multiple device types
- Real message flow
- Will demonstrate key patterns

---

### Slide 23: Demo Scene 1 - Startup

**Terminal recording placeholder**

```
$ docker-compose up -d nats gateway
Starting nats-server ...
Starting gateway ...

$ ./demo/run_devices.sh
[temp-sensor-001] Connecting to gateway...
[temp-sensor-001] Authenticated. Permissions: pub=[factory.line1.temp]
[conveyor-001] Connecting to gateway...
[conveyor-001] Authenticated. Subscribed to: factory.line1.conveyor.cmd
[vision-001] Connecting...
[estop-001] Connecting...

All devices connected. Ready for operation.
```

**Talking Points:**
- NATS starts in seconds
- Gateway connects to NATS
- Devices authenticate and receive permissions
- Each device knows what it can do

---

### Slide 24: Demo Scene 2 - Normal Operation

**Terminal recording placeholder**

```
$ nats sub "factory.line1.>"

[factory.line1.temp] {"value": 23.4, "unit": "celsius"}
[factory.line1.conveyor.status] {"speed": 100, "mode": "running"}
[factory.line1.quality.result] {"pass": true, "scan_id": 1042}
[factory.line1.output.count] {"good": 1042, "reject": 3}
[factory.line1.temp] {"value": 23.5, "unit": "celsius"}
...
```

**Talking Points:**
- All telemetry flows through NATS subjects
- Hierarchical subject naming
- Each device publishes to its allowed topics
- Any consumer can subscribe with wildcards

---

### Slide 25: Demo Scene 3 - Send Command

**Terminal recording placeholder**

```
# From HMI Panel

> setSpeed 150

[HMI] Publishing: factory.line1.conveyor.cmd {"action": "setSpeed", "value": 150}
[conveyor-001] Received command: setSpeed to 150
[conveyor-001] Ramping speed: 100 -> 110 -> 120 -> 130 -> 140 -> 150
[conveyor-001] Target speed reached: 150 units/min
[factory.line1.conveyor.status] {"speed": 150, "mode": "running"}
```

**Talking Points:**
- Bidirectional communication
- HMI publishes to command topic
- Conveyor subscribed to that topic
- Status update confirms the change
- Same gateway, same protocol

---

### Slide 26: Demo Scene 4 - Reliability

**Terminal recording placeholder**

```
# Kill the conveyor process

$ kill -9 $(pgrep conveyor)

[gateway] Device conveyor-001 disconnected

# Meanwhile, commands keep publishing...
[factory.line1.conveyor.cmd] {"action": "setSpeed", "value": 120}
[factory.line1.conveyor.cmd] {"action": "setSpeed", "value": 100}

# JetStream stores these messages

# Conveyor reconnects (auto-reconnect with backoff)
[conveyor-001] Connection lost. Reconnecting in 1s...
[conveyor-001] Reconnecting in 2s...
[conveyor-001] Connected! Replaying missed messages...
[conveyor-001] Received command: setSpeed to 120
[conveyor-001] Received command: setSpeed to 100
[conveyor-001] Caught up. No messages lost.
```

**Talking Points:**
- Device disconnection is inevitable
- SDK handles reconnection automatically
- JetStream stored messages during disconnect
- Messages replayed in order
- **No data loss**

---

### Slide 27: Demo Scene 5 - Emergency Broadcast

**Terminal recording placeholder**

```
# E-Stop button pressed

[estop-001] EMERGENCY STOP TRIGGERED
[estop-001] Publishing to: factory.line1.emergency

# All devices receive simultaneously (fan-out)

[conveyor-001] Emergency received! Halting motor...
[vision-001] Emergency received! Stopping scanner...
[counter-001] Emergency received! Pausing count...

Broadcast latency: < 50ms to all devices
```

**Talking Points:**
- One publish, multiple subscribers
- NATS fan-out is extremely fast
- Subject-based routing enables broadcast
- Safety-critical messaging

---

## Section 7: ARCHITECTURE PATTERNS RECAP (17:00 - 18:30)

### Slide 28: Pattern 1 - Gateway as Security Boundary

**Visual: Security zones**

```
┌─────────────────────────────────────────────────────────────┐
│  PATTERN: Gateway as Security Boundary                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   UNTRUSTED              GATEWAY              TRUSTED       │
│                                                             │
│   ┌─────────┐         ┌─────────┐         ┌─────────┐      │
│   │ Devices │────────►│ Enforce │────────►│  NATS   │      │
│   │         │         │ Policy  │         │         │      │
│   └─────────┘         └─────────┘         └─────────┘      │
│                                                             │
│   When to use:                                              │
│   • Devices on untrusted networks                           │
│   • Need protocol translation                               │
│   • Require per-device authorization                        │
│   • Want centralized security policy                        │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 29: Pattern 2 - Subject Hierarchy for Authorization

**Visual: Subject structure**

```
┌─────────────────────────────────────────────────────────────┐
│  PATTERN: Subject Hierarchy for Authorization                │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   Structure:  {domain}.{entity}.{action}                    │
│                                                             │
│   Examples:                                                 │
│     factory.line1.temp           →  Temperature readings    │
│     factory.line1.conveyor.cmd   →  Conveyor commands       │
│     factory.line1.alerts.*       →  All alerts on line 1    │
│     factory.>                    →  Everything in factory   │
│                                                             │
│   Benefits:                                                 │
│   • Natural hierarchy maps to organization                  │
│   • Wildcards enable flexible subscriptions                 │
│   • Fine-grained authorization possible                     │
│   • Easy to audit and understand                            │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 30: Pattern 3 - JetStream for Reliability

**Visual: When to use what**

```
┌─────────────────────────────────────────────────────────────┐
│  PATTERN: JetStream for Reliability                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   Core NATS (Fire & Forget)     JetStream (Persistent)      │
│   ─────────────────────────     ────────────────────────    │
│   • Metrics/telemetry           • Commands                  │
│   • Heartbeats                  • Alerts                    │
│   • Real-time dashboards        • Audit events              │
│   • Loss is acceptable          • Loss is NOT acceptable    │
│                                                             │
│   Key JetStream Features:                                   │
│   ✓ At-least-once delivery                                  │
│   ✓ Message replay from any point                           │
│   ✓ Consumer acknowledgments                                │
│   ✓ Retention policies (time, size, count)                  │
│   ✓ Stream replication for HA                               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 31: Pattern 4 - SDK for Standardization

**Visual: SDK benefits**

```
┌─────────────────────────────────────────────────────────────┐
│  PATTERN: SDK for Standardization                            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   Without SDK:              With SDK:                       │
│   ────────────              ─────────                       │
│   100 integrations          100 integrations                │
│   100 implementations       1 implementation                │
│   100 bug surfaces          1 bug surface                   │
│   100x support burden       1x support burden               │
│                                                             │
│   SDK provides:                                             │
│   ✓ Consistent behavior across all devices                  │
│   ✓ Built-in reliability (reconnection, buffering)          │
│   ✓ Security handled correctly                              │
│   ✓ Vendors integrate in hours, not weeks                   │
│   ✓ Updates pushed to all devices via SDK release           │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## Section 8: CALL TO ACTION (18:30 - 19:30)

### Slide 32: Try NATS Today

**Visual: Getting started commands**

```
┌─────────────────────────────────────────────────────────────┐
│  GET STARTED IN 60 SECONDS                                   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   # Start NATS with JetStream                               │
│   $ docker run -p 4222:4222 nats -js                        │
│                                                             │
│   # In another terminal, subscribe                          │
│   $ nats sub ">"                                            │
│                                                             │
│   # In another terminal, publish                            │
│   $ nats pub hello "world"                                  │
│                                                             │
│   That's it. You're using NATS.                             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 33: Production Ready

**Visual: Enterprise adoption**

```
┌─────────────────────────────────────────────────────────────┐
│  NATS IN PRODUCTION                                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ✓ CNCF Incubating Project                                 │
│   ✓ 15,000+ GitHub stars                                    │
│   ✓ Active community                                        │
│                                                             │
│   Used by:                                                  │
│   • Salesforce           • VMware                           │
│   • Mastercard           • Netlify                          │
│   • Clarifai             • Choria                           │
│                                                             │
│   License: Apache 2.0 (Free, forever)                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 34: Resources

**Visual: Links and QR codes**

```
┌─────────────────────────────────────────────────────────────┐
│  RESOURCES                                                   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   NATS Documentation                                        │
│   https://docs.nats.io                                      │
│                                                             │
│   This Project (Gateway + SDK + Demo)                       │
│   https://github.com/[your-repo]                            │
│                                                             │
│   NATS Slack Community                                      │
│   https://slack.nats.io                                     │
│                                                             │
│   JetStream Deep Dive                                       │
│   https://docs.nats.io/nats-concepts/jetstream              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## Section 9: OUTRO (19:30 - 20:00)

### Slide 35: Summary

**Visual: Three-layer recap**

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│                      THE PATTERN                            │
│                                                             │
│   ┌─────────────┐     ┌─────────────┐     ┌─────────────┐   │
│   │     SDK     │────►│   GATEWAY   │────►│    NATS     │   │
│   │             │     │             │     │             │   │
│   │ Consistency │     │  Security   │     │ Reliability │   │
│   └─────────────┘     └─────────────┘     └─────────────┘   │
│                                                             │
│   Works for: IoT, microservices, real-time apps,            │
│              edge computing, event-driven architectures     │
│                                                             │
│            Thanks for watching!                             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

### Slide 36: End Card

**Visual: Channel branding**

- Subscribe reminder
- Like button reminder
- Link to related videos
- Social media handles

---

## Notes for Slide Creation

### Design Guidelines

1. **Color Scheme:**
   - Primary: NATS blue (#27AAE1)
   - Secondary: Green for success/SDK (#4CAF50)
   - Accent: Purple for gateway (#9C27B0)
   - Background: Dark (#1E1E1E) or Light (#FFFFFF)

2. **Typography:**
   - Headers: Bold, 36-48pt
   - Body: Regular, 24-28pt
   - Code: Monospace, 18-20pt

3. **Animations:**
   - Fade in for text
   - Build for lists
   - Highlight for code snippets
   - Avoid excessive motion

4. **Consistency:**
   - Same diagram style throughout
   - Same code highlighting
   - Same icon set

### Tools Recommended

- **Slides:** PowerPoint, Keynote, or Google Slides
- **Diagrams:** Draw.io, Excalidraw, or Mermaid CLI export
- **Code Screenshots:** Carbon.now.sh or custom syntax highlighting
- **Terminal Recordings:** asciinema or custom recording with OBS
