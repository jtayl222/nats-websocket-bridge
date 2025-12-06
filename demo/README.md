# Smart Manufacturing Cell Demo

## Pharmaceutical Packaging Line POC

This demo simulates a complete pharmaceutical packaging line with multiple connected devices, demonstrating the capabilities of the NATS WebSocket Bridge Gateway and C++ Device SDK.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PACKAGING LINE 1                                      │
│                                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │  Temperature │  │   Conveyor   │  │   Vision     │  │  Production  │    │
│  │    Sensor    │  │  Controller  │  │   Scanner    │  │   Counter    │    │
│  │              │  │              │  │              │  │              │    │
│  │ Publishes:   │  │ Listens:     │  │ Publishes:   │  │ Publishes:   │    │
│  │ temp readings│  │ cmd.conveyor │  │ rejects      │  │ output count │    │
│  │              │  │ Publishes:   │  │ quality.stats│  │              │    │
│  │              │  │ status       │  │              │  │              │    │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘    │
│         │                 │                 │                 │             │
│         │                 │                 │                 │             │
│  ┌──────┴─────────────────┴─────────────────┴─────────────────┴──────┐     │
│  │                        E-Stop Button                               │     │
│  │                   (Broadcasts to all devices)                      │     │
│  └────────────────────────────┬───────────────────────────────────────┘     │
│                               │                                              │
└───────────────────────────────┼──────────────────────────────────────────────┘
                                │
                    ┌───────────▼───────────┐
                    │   WebSocket Gateway   │
                    │                       │
                    │  - Authentication     │
                    │  - Authorization      │
                    │  - Rate limiting      │
                    │  - Message routing    │
                    └───────────┬───────────┘
                                │
                    ┌───────────▼───────────┐
                    │   NATS + JetStream    │
                    │                       │
                    │  - Persistence        │
                    │  - Replay             │
                    │  - Durability         │
                    └───────────────────────┘
                                │
          ┌─────────────────────┼─────────────────────┐
          │                     │                     │
┌─────────▼─────────┐ ┌─────────▼─────────┐ ┌─────────▼─────────┐
│  Line Orchestrator │ │    HMI Panel      │ │  Other Systems    │
│                    │ │                   │ │  (SCADA, MES...)  │
│  - Coordination    │ │  - Operator UI    │ │                   │
│  - OEE Calculation │ │  - Commands       │ │                   │
│  - Alerts          │ │  - Monitoring     │ │                   │
└────────────────────┘ └───────────────────┘ └───────────────────┘
```

## Devices

### 1. Temperature Sensor (`sensor-temp-001`)
- Monitors packaging room temperature
- Publishes to `factory.line1.temp`
- Threshold alerts at 75°F (warning) and 80°F (critical)
- Supports anomaly injection for demo

### 2. Conveyor Belt Controller (`actuator-conveyor-001`)
- Controls main packaging conveyor
- Listens to `factory.line1.conveyor.cmd`
- Publishes status to `factory.line1.conveyor.status`
- Commands: start, stop, setSpeed, emergency_stop, reset

### 3. Vision Quality Scanner (`sensor-vision-001`)
- Optical inspection system
- Publishes rejects to `factory.line1.quality.rejects`
- Publishes stats to `factory.line1.quality.stats`
- Detects: label issues, damage, contamination, barcode problems

### 4. Emergency Stop Button (`sensor-estop-001`)
- Physical E-Stop simulation
- Publishes to `factory.line1.eStop`
- Broadcasts to `factory.line1.emergency` (fan-out to all devices)
- Latching behavior with reset

### 5. Production Counter (`sensor-counter-001`)
- Photoelectric package counter
- Publishes to `factory.line1.output`
- Tracks good count, rejects, yield
- Batch completion notification

### 6. Line Orchestrator (`controller-orchestrator-001`)
- Central PLC/controller
- Aggregates all device status
- Calculates OEE (Overall Equipment Effectiveness)
- Coordinates start/stop sequences
- Publishes line status and OEE metrics

### 7. HMI Panel (`hmi-panel-001`)
- Operator interface simulation
- Real-time dashboard display
- Interactive command menu
- Alert display

## Message Subjects

| Subject | Direction | Description |
|---------|-----------|-------------|
| `factory.line1.temp` | Device → Gateway | Temperature readings |
| `factory.line1.conveyor.cmd` | Gateway → Device | Conveyor commands |
| `factory.line1.conveyor.status` | Device → Gateway | Conveyor status |
| `factory.line1.quality.rejects` | Device → Gateway | Reject events |
| `factory.line1.quality.stats` | Device → Gateway | Quality statistics |
| `factory.line1.eStop` | Device → Gateway | E-Stop events |
| `factory.line1.emergency` | Broadcast | Emergency notifications |
| `factory.line1.output` | Device → Gateway | Production counts |
| `factory.line1.status` | Device → Gateway | Line status |
| `factory.line1.oee` | Device → Gateway | OEE metrics |
| `factory.line1.alerts.*` | Device → Gateway | Alerts by severity |

## Building

```bash
cd demo/devices
mkdir build && cd build
cmake ..
cmake --build .
```

## Running

### Option 1: Demo Script

```bash
./scripts/run_demo.sh --all
```

### Option 2: Individual Devices

Start in separate terminals:

```bash
# Terminal 1: Gateway (must be running first)
cd src/NatsWebSocketBridge.Gateway
dotnet run

# Terminal 2: Orchestrator
./build/line_orchestrator

# Terminal 3: Conveyor
./build/conveyor_controller

# Terminal 4: Temperature
./build/temperature_sensor

# Terminal 5: Vision Scanner
./build/vision_scanner

# Terminal 6: Counter
./build/production_counter

# Terminal 7: E-Stop
./build/estop_button

# Terminal 8: HMI
./build/hmi_panel
```

## Demo Scenarios

### Scenario 1: Normal Production

1. Start all devices
2. In HMI, press `[1]` to start line
3. Watch conveyor ramp up
4. Observe production counter incrementing
5. Monitor OEE metrics

### Scenario 2: Temperature Anomaly

1. With line running, inject temperature spike:
   ```json
   {"action": "inject_anomaly", "magnitude": 10, "duration": 30000}
   ```
2. Watch temperature readings spike
3. Alert appears when threshold exceeded
4. Temperature returns to normal after duration

### Scenario 3: Quality Problem

1. Inject high defect rate:
   ```json
   {"action": "inject_high_defects", "rate": 0.20}
   ```
2. Watch reject count climb rapidly
3. Consecutive reject alert triggers
4. Defect rate alert when threshold exceeded

### Scenario 4: Emergency Stop

1. Press `ENTER` in E-Stop terminal
2. All devices receive emergency broadcast
3. Conveyor stops immediately
4. Line state changes to "emergency"
5. Type `reset` to clear

### Scenario 5: Device Reconnection

1. Kill conveyor controller (Ctrl+C)
2. Wait 10-15 seconds
3. Restart conveyor controller
4. Watch automatic reconnection
5. Device receives last known state from JetStream

### Scenario 6: Gateway Restart

1. Stop the gateway
2. Watch devices attempt reconnection
3. Start gateway
4. Devices reconnect automatically
5. JetStream consumers resume

## What This Proves

| Feature | Demonstrated |
|---------|--------------|
| SDK connectivity | Devices connect via C++ SDK |
| Authentication | Token-based auth for each device |
| Telemetry | Continuous sensor data streaming |
| Commands | Bidirectional command/response |
| JetStream persistence | Messages stored durably |
| Message replay | State recovery after reconnect |
| Multiple device types | Sensors, actuators, controllers |
| WebSocket gateway | Protocol bridge to NATS |
| Fan-out/broadcast | E-Stop reaches all devices |
| Subscriptions | Wildcard patterns working |
| Alerting | Threshold-based alerts |
| OEE calculation | Industrial KPI aggregation |

## Configuration

Edit `config/demo_config.json` to customize:
- Gateway URL
- Device credentials
- Batch information
- Alert thresholds
- OEE parameters

## Troubleshooting

### Devices not connecting
- Ensure gateway is running
- Check GATEWAY_URL environment variable
- Verify tokens in gateway configuration

### No data flowing
- Check conveyor is running (press start in HMI)
- Vision scanner needs conveyor running to scan
- Counter needs conveyor speed > 0

### HMI shows unknown
- Wait for first status update cycle (10s)
- Ensure orchestrator is running
- Check subscriptions are active

## Pharmaceutical Compliance Notes

This demo illustrates concepts relevant to:
- **21 CFR Part 11**: Audit trails via JetStream
- **Batch traceability**: Lot/batch tracking in all messages
- **OEE reporting**: Industry-standard metrics
- **Alert management**: Severity-based escalation
