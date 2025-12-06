# Demo Recording Scripts

Step-by-step scripts for recording the live demo portions of the video.

---

## Pre-Recording Checklist

### Environment Setup

```bash
# Terminal settings
export PS1='$ '          # Simple prompt
export TERM=xterm-256color
stty columns 100         # Consistent width
stty rows 30             # Consistent height

# Font: 18pt+ monospace (Fira Code, JetBrains Mono, or SF Mono)
# Theme: Dark with high contrast
# Window: 1920x1080 or 4K capture area
```

### Required Services

```bash
# Verify NATS is ready
docker ps | grep nats

# Verify Gateway builds
cd /path/to/nats-websocket-bridge
dotnet build src/NatsWebSocketBridge.Gateway

# Verify Demo devices build
cd demo/devices
mkdir -p build && cd build
cmake .. && make
```

### tmux Layout (Recommended)

```
┌─────────────────────────────────────────────────────────────┐
│ Pane 0: Gateway Logs                                        │
├────────────────────────────────┬────────────────────────────┤
│ Pane 1: NATS Monitor           │ Pane 2: Device Logs        │
├────────────────────────────────┼────────────────────────────┤
│ Pane 3: Commands               │ Pane 4: HMI Panel          │
└────────────────────────────────┴────────────────────────────┘
```

Setup script:
```bash
tmux new-session -s demo -d
tmux split-window -v -t demo
tmux split-window -h -t demo:0.1
tmux split-window -h -t demo:0.0
tmux select-pane -t demo:0.0
tmux attach -t demo
```

---

## Scene 1: System Startup (1 minute)

### Objective
Show how quickly NATS and the Gateway start, then demonstrate device authentication.

### Script

**Pane 0 (Gateway):**
```bash
# Clear and title
clear
echo "=== GATEWAY SERVER ==="
echo ""

# Start the gateway
cd /path/to/nats-websocket-bridge
dotnet run --project src/NatsWebSocketBridge.Gateway
```

Expected output:
```
info: NatsWebSocketBridge.Gateway[0]
      WebSocket Gateway starting...
info: NatsWebSocketBridge.Gateway[0]
      Connected to NATS at nats://localhost:4222
info: NatsWebSocketBridge.Gateway[0]
      JetStream initialized. Streams: TELEMETRY, EVENTS, ALERTS
info: Microsoft.Hosting.Lifetime[0]
      Now listening on: http://localhost:5000
info: NatsWebSocketBridge.Gateway[0]
      Gateway ready. Waiting for device connections...
```

**Pane 1 (NATS Monitor):**
```bash
clear
echo "=== NATS MESSAGES ==="
echo ""

# Subscribe to all messages
nats sub "factory.line1.>"
```

**Pane 2 (Device Logs):**
```bash
clear
echo "=== DEVICES ==="
echo ""

# Start devices one by one with slight delay
./demo/devices/build/temperature_sensor &
sleep 1
./demo/devices/build/conveyor_controller &
sleep 1
./demo/devices/build/vision_scanner &
sleep 1
./demo/devices/build/estop_button &
```

Expected output per device:
```
[INFO] [temp-sensor-001] Connecting to ws://localhost:5000/ws
[INFO] [temp-sensor-001] WebSocket connected
[INFO] [temp-sensor-001] Authenticating...
[INFO] [temp-sensor-001] Authentication successful
[INFO] [temp-sensor-001] Permissions received:
       Publish: factory.line1.temp, factory.line1.alerts.*
       Subscribe: factory.line1.cmd.*
[INFO] [temp-sensor-001] Ready
```

### Voiceover Script

> "Let's start the system. First, I'll bring up the NATS server and gateway. Notice it connects to NATS and initializes JetStream streams in under a second.
>
> Now let's start our devices. Each device connects via WebSocket, authenticates with a token, and receives its permissions. The temperature sensor can publish to its telemetry topic and alerts, but can only subscribe to commands meant for it.
>
> This is the gateway enforcing the principle of least privilege. Each device only has access to what it needs."

---

## Scene 2: Normal Operation (1 minute)

### Objective
Show telemetry flowing through the system, demonstrating subject-based routing.

### Script

**Pane 1 (NATS Monitor):** Already subscribed from Scene 1, messages flowing:

```
[#1] Received on "factory.line1.temp"
{"deviceId":"temp-sensor-001","value":23.4,"unit":"celsius","timestamp":"2024-01-15T10:30:00Z"}

[#2] Received on "factory.line1.conveyor.status"
{"deviceId":"conveyor-001","speed":100,"mode":"running","timestamp":"2024-01-15T10:30:01Z"}

[#3] Received on "factory.line1.quality.result"
{"deviceId":"vision-001","pass":true,"scanId":1001,"timestamp":"2024-01-15T10:30:02Z"}

[#4] Received on "factory.line1.temp"
{"deviceId":"temp-sensor-001","value":23.5,"unit":"celsius","timestamp":"2024-01-15T10:30:05Z"}
```

**Pane 3 (Commands):** Show subject filtering
```bash
# Show only temperature readings
echo "Filtering to temperature only:"
nats sub "factory.line1.temp" --count=3
```

```bash
# Show only quality results
echo "Filtering to quality results:"
nats sub "factory.line1.quality.>" --count=3
```

### Voiceover Script

> "Now the system is running. Watch the NATS monitor - every device is publishing to its designated subject. Temperature readings go to factory.line1.temp. Conveyor status to factory.line1.conveyor.status.
>
> The beauty of subject-based routing is filtering. If I only want temperature readings, I subscribe to that specific subject. If I want all quality-related messages, I use the wildcard factory.line1.quality.>.
>
> This is how enterprise systems like SCADA or historians connect - they subscribe to exactly what they need."

---

## Scene 3: Send a Command (1 minute)

### Objective
Demonstrate bidirectional communication through the gateway.

### Script

**Pane 4 (HMI):**
```bash
clear
echo "=== HMI CONTROL PANEL ==="
./demo/hmi/build/hmi_panel
```

Interactive session:
```
HMI Panel Ready
Connected to Gateway

Commands:
  1. Start Line
  2. Stop Line
  3. Set Conveyor Speed
  4. Emergency Stop
  5. View Status
  q. Quit

Enter command: 3
Enter speed (50-200): 150

[HMI] Sending command: setSpeed 150
[HMI] Publishing to: factory.line1.conveyor.cmd
```

**Pane 2 (Device Logs):** Conveyor receives command
```
[INFO] [conveyor-001] Received command: setSpeed
[INFO] [conveyor-001] Changing speed: 100 -> 150
[INFO] [conveyor-001] Ramping: 100 -> 110 -> 120 -> 130 -> 140 -> 150
[INFO] [conveyor-001] Target speed reached: 150 units/min
```

**Pane 1 (NATS):** Shows the command and status update
```
[#42] Received on "factory.line1.conveyor.cmd"
{"action":"setSpeed","value":150,"source":"hmi-panel-001"}

[#43] Received on "factory.line1.conveyor.status"
{"deviceId":"conveyor-001","speed":150,"mode":"running","previousSpeed":100}
```

### Voiceover Script

> "Now let's send a command. From the HMI panel, I'll change the conveyor speed to 150.
>
> Watch what happens: The HMI publishes a command to factory.line1.conveyor.cmd. The conveyor is subscribed to that subject, so it receives the command. It ramps the speed up and publishes its new status.
>
> This is bidirectional communication. Commands go down to devices, status comes back up. Same gateway, same protocol, same security model."

---

## Scene 4: Reliability Demo (2 minutes)

### Objective
Demonstrate automatic reconnection and JetStream message replay.

### Script

**Setup:** First, ensure JetStream is storing messages
```bash
# Verify stream exists
nats stream info EVENTS
```

**Pane 2 (Device Logs):** Find conveyor PID and kill it
```bash
# Get the PID
CONVEYOR_PID=$(pgrep -f conveyor_controller)
echo "Conveyor PID: $CONVEYOR_PID"

# Kill it suddenly (simulating network failure)
kill -9 $CONVEYOR_PID
```

Expected output in Gateway logs (Pane 0):
```
warn: NatsWebSocketBridge.Gateway[0]
      Device disconnected: conveyor-001
info: NatsWebSocketBridge.Gateway[0]
      Active connections: 3
```

**Pane 3 (Commands):** Send commands while device is down
```bash
# These will be stored in JetStream
echo "Sending commands while conveyor is offline..."

nats pub factory.line1.conveyor.cmd '{"action":"setSpeed","value":120}'
sleep 1
nats pub factory.line1.conveyor.cmd '{"action":"setSpeed","value":100}'
sleep 1
nats pub factory.line1.conveyor.cmd '{"action":"status"}'

echo "Commands sent to JetStream"
```

**Pane 2 (Device Logs):** Restart conveyor
```bash
# Wait a moment, then restart
sleep 5
./demo/devices/build/conveyor_controller
```

Expected output:
```
[INFO] [conveyor-001] Connecting to ws://localhost:5000/ws
[INFO] [conveyor-001] WebSocket connected
[INFO] [conveyor-001] Authenticating...
[INFO] [conveyor-001] Authentication successful
[INFO] [conveyor-001] Resubscribing to: factory.line1.conveyor.cmd
[INFO] [conveyor-001] JetStream replay starting...
[INFO] [conveyor-001] Received command: setSpeed 120
[INFO] [conveyor-001] Received command: setSpeed 100
[INFO] [conveyor-001] Received command: status
[INFO] [conveyor-001] Replay complete. 3 messages processed.
[INFO] [conveyor-001] Ready - current speed: 100
```

### Alternative: SDK Auto-Reconnect Demo

If showing SDK reconnection:
```bash
# In device code, briefly disconnect network
# SDK output shows:
[WARN] [conveyor-001] Connection lost
[INFO] [conveyor-001] Reconnecting in 1000ms...
[WARN] [conveyor-001] Reconnect attempt 1 failed
[INFO] [conveyor-001] Reconnecting in 2000ms...
[INFO] [conveyor-001] Reconnect attempt 2 succeeded
[INFO] [conveyor-001] Re-authenticated
[INFO] [conveyor-001] Replaying missed messages...
```

### Voiceover Script

> "Here's where it gets interesting. Let me simulate a device failure by killing the conveyor process.
>
> The gateway detects the disconnect immediately. But watch - I'm still sending commands. These commands are going to JetStream, which persists them.
>
> Now let's restart the conveyor. It reconnects, re-authenticates, and - here's the key part - replays the messages it missed. Three commands were sent while it was offline. All three are delivered in order.
>
> This is JetStream's killer feature: guaranteed delivery with replay. Devices can disconnect, networks can fail, but messages are never lost."

---

## Scene 5: Emergency Broadcast (1 minute)

### Objective
Demonstrate fan-out pattern with emergency stop broadcast.

### Script

**Pane 4 (E-Stop):** Run the E-Stop device interactively
```bash
./demo/devices/build/estop_button --interactive
```

Output:
```
E-Stop Button Ready
Press ENTER to trigger emergency stop...
```

Press Enter:
```
[ALERT] EMERGENCY STOP TRIGGERED
[INFO] Broadcasting to: factory.line1.emergency
```

**Pane 2 (Device Logs):** All devices receive simultaneously
```
[ALERT] [conveyor-001] EMERGENCY STOP RECEIVED
[ALERT] [conveyor-001] Motor halting immediately
[INFO] [conveyor-001] Speed: 100 -> 0 (immediate)

[ALERT] [vision-001] EMERGENCY STOP RECEIVED
[ALERT] [vision-001] Scanner stopped

[ALERT] [counter-001] EMERGENCY STOP RECEIVED
[ALERT] [counter-001] Count frozen: 1042
```

**Pane 1 (NATS):** Shows the broadcast message
```
[#87] Received on "factory.line1.emergency"
{"type":"estop","source":"estop-001","timestamp":"2024-01-15T10:35:00Z"}
```

**Timing verification:**
```bash
# Show the latency
echo "Broadcast delivered to all devices in < 50ms"
```

### Reset Sequence
```bash
# From HMI
Enter command: 6  # Reset E-Stop

[HMI] Publishing reset command
[INFO] [conveyor-001] E-Stop cleared. Ready for restart.
[INFO] [vision-001] E-Stop cleared. Ready.
```

### Voiceover Script

> "For our final demo: emergency stop. This is a safety-critical broadcast pattern.
>
> When I press the E-Stop button, it publishes a single message to factory.line1.emergency. Every device subscribed to that subject receives it simultaneously.
>
> Watch the timing - all devices respond within 50 milliseconds. The conveyor halts immediately. The scanner stops. The counter freezes.
>
> This is NATS fan-out in action. One publish, instant delivery to all subscribers. For safety-critical applications, this kind of latency matters."

---

## Scene 6: OEE Dashboard (Optional, 1 minute)

### Objective
Show real-time metrics aggregation.

### Script

**Pane 4 (HMI):**
```bash
# Show OEE dashboard
Enter command: 7  # View OEE

┌─────────────────────────────────────────────┐
│            OEE DASHBOARD                     │
├─────────────────────────────────────────────┤
│  Availability:  93.75%  ████████████░░      │
│  Performance:   92.00%  ███████████░░░      │
│  Quality:       98.50%  █████████████████░  │
├─────────────────────────────────────────────┤
│  OEE:           84.87%  █████████████░░░    │
│                                             │
│  Shift: 7.5 hours   Downtime: 30 min        │
│  Good: 43,500       Reject: 660             │
│  Rate: 92/min       Target: 100/min         │
└─────────────────────────────────────────────┘
```

### Voiceover Script

> "All this telemetry can be aggregated into real-time metrics. Here's an OEE dashboard - Overall Equipment Effectiveness. Availability times Performance times Quality.
>
> The data feeding this comes from the same NATS subjects we've been watching. Temperature, conveyor status, quality results - all aggregated in real-time."

---

## Recording Tips

### General
- **Practice each scene** 3-4 times before recording
- **Use keyboard macros** for complex commands
- **Have fallback recordings** ready for demos that might fail
- **Record each scene separately** for easier editing
- **Leave 2-3 second pauses** between actions for editing

### Terminal Recording Tools

**Option 1: asciinema**
```bash
# Record
asciinema rec scene1.cast

# Play back
asciinema play scene1.cast

# Export to GIF
agg scene1.cast scene1.gif
```

**Option 2: terminalizer**
```bash
# Install
npm install -g terminalizer

# Record
terminalizer record scene1

# Render
terminalizer render scene1 -o scene1.gif
```

**Option 3: OBS with terminal**
- Capture terminal window directly
- Add slight zoom for readability
- Use 1080p or 4K capture

### Timing Guide

| Scene | Duration | Key Moments |
|-------|----------|-------------|
| 1. Startup | 60s | Gateway start (0:10), Device auth (0:30), All ready (0:50) |
| 2. Normal | 60s | Messages flowing (0:15), Filter demo (0:35) |
| 3. Command | 60s | Send command (0:15), Device response (0:30), Status confirm (0:45) |
| 4. Reliability | 120s | Kill device (0:15), Send commands (0:45), Reconnect (1:00), Replay (1:30) |
| 5. E-Stop | 60s | Press button (0:15), All devices stop (0:25), Timing shown (0:40) |

### Backup Plans

If live demo fails:
1. Have pre-recorded video of successful run
2. Switch to narrated slides showing the sequence
3. Use animated diagrams to explain what would happen

### Post-Production

1. Add subtle zoom on key terminal output
2. Highlight important messages with boxes/arrows
3. Add captions for key commands
4. Ensure font is readable at 720p (minimum)
5. Add background music (very subtle, 5-10% volume)
