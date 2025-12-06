#!/bin/bash

# Packaging Line Demo Runner
# This script starts all demo devices in separate terminal windows

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEMO_DIR="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$DEMO_DIR/devices/build"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_banner() {
    echo -e "${BLUE}"
    echo "╔═══════════════════════════════════════════════════════════════╗"
    echo "║     NATS WebSocket Gateway - Packaging Line Demo              ║"
    echo "║     Smart Manufacturing Cell POC                              ║"
    echo "╚═══════════════════════════════════════════════════════════════╝"
    echo -e "${NC}"
}

check_prerequisites() {
    echo -e "${YELLOW}Checking prerequisites...${NC}"

    # Check if binaries exist
    if [ ! -d "$BUILD_DIR" ]; then
        echo -e "${RED}Build directory not found. Please build first:${NC}"
        echo "  cd $DEMO_DIR/devices"
        echo "  mkdir build && cd build"
        echo "  cmake .."
        echo "  cmake --build ."
        exit 1
    fi

    # Check for required binaries
    BINARIES=("temperature_sensor" "conveyor_controller" "vision_scanner" "estop_button" "production_counter" "line_orchestrator")
    for bin in "${BINARIES[@]}"; do
        if [ ! -f "$BUILD_DIR/$bin" ]; then
            echo -e "${RED}Missing binary: $bin${NC}"
            echo "Please build the demo first."
            exit 1
        fi
    done

    echo -e "${GREEN}All binaries found.${NC}"
}

# Detect terminal emulator
detect_terminal() {
    if command -v gnome-terminal &> /dev/null; then
        echo "gnome-terminal"
    elif command -v xterm &> /dev/null; then
        echo "xterm"
    elif command -v konsole &> /dev/null; then
        echo "konsole"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        echo "osascript"
    else
        echo "none"
    fi
}

# Open terminal with command
open_terminal() {
    local title="$1"
    local cmd="$2"
    local term=$(detect_terminal)

    case $term in
        gnome-terminal)
            gnome-terminal --title="$title" -- bash -c "$cmd; read -p 'Press Enter to close...'"
            ;;
        xterm)
            xterm -T "$title" -e "$cmd; read -p 'Press Enter to close...'" &
            ;;
        konsole)
            konsole --new-tab -p tabtitle="$title" -e bash -c "$cmd; read -p 'Press Enter to close...'"
            ;;
        osascript)
            osascript -e "tell application \"Terminal\" to do script \"$cmd\""
            ;;
        *)
            echo -e "${YELLOW}Cannot open terminal for: $title${NC}"
            echo "  Run manually: $cmd"
            ;;
    esac
}

start_devices() {
    echo -e "${YELLOW}Starting devices...${NC}"
    echo ""

    # Start order matters for proper initialization

    echo -e "${GREEN}[1/7] Starting Line Orchestrator...${NC}"
    open_terminal "Orchestrator" "$BUILD_DIR/line_orchestrator"
    sleep 1

    echo -e "${GREEN}[2/7] Starting Temperature Sensor...${NC}"
    open_terminal "Temperature Sensor" "$BUILD_DIR/temperature_sensor"
    sleep 0.5

    echo -e "${GREEN}[3/7] Starting Conveyor Controller...${NC}"
    open_terminal "Conveyor Controller" "$BUILD_DIR/conveyor_controller"
    sleep 0.5

    echo -e "${GREEN}[4/7] Starting Vision Scanner...${NC}"
    open_terminal "Vision Scanner" "$BUILD_DIR/vision_scanner"
    sleep 0.5

    echo -e "${GREEN}[5/7] Starting Production Counter...${NC}"
    open_terminal "Production Counter" "$BUILD_DIR/production_counter"
    sleep 0.5

    echo -e "${GREEN}[6/7] Starting E-Stop Button...${NC}"
    open_terminal "E-Stop Button" "$BUILD_DIR/estop_button"
    sleep 0.5

    echo -e "${GREEN}[7/7] Starting HMI Panel...${NC}"
    open_terminal "HMI Panel" "$BUILD_DIR/hmi_panel"

    echo ""
    echo -e "${GREEN}All devices started!${NC}"
}

show_menu() {
    echo ""
    echo -e "${YELLOW}Demo Scenarios:${NC}"
    echo "  1. Start all devices"
    echo "  2. Start production (conveyor + sensors only)"
    echo "  3. Start HMI only (monitor mode)"
    echo "  4. Show demo script"
    echo "  5. Exit"
    echo ""
    read -p "Select option: " choice

    case $choice in
        1) start_devices ;;
        2)
            open_terminal "Conveyor" "$BUILD_DIR/conveyor_controller"
            sleep 0.5
            open_terminal "Temp Sensor" "$BUILD_DIR/temperature_sensor"
            sleep 0.5
            open_terminal "Counter" "$BUILD_DIR/production_counter"
            ;;
        3) open_terminal "HMI" "$BUILD_DIR/hmi_panel" ;;
        4) show_demo_script ;;
        5) exit 0 ;;
        *) echo "Invalid option" ;;
    esac
}

show_demo_script() {
    echo ""
    echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
    echo -e "${YELLOW}DEMO SCENARIO SCRIPT${NC}"
    echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
    echo ""
    echo "1. START THE LINE"
    echo "   - In HMI, press [1] to start line"
    echo "   - Watch conveyor ramp up"
    echo "   - Production counter starts incrementing"
    echo ""
    echo "2. DEMONSTRATE TELEMETRY"
    echo "   - Observe temperature readings every 5s"
    echo "   - Watch quality stats update"
    echo "   - Note OEE calculation"
    echo ""
    echo "3. INJECT TEMPERATURE ANOMALY"
    echo "   - Send command to temperature sensor:"
    echo "     {\"action\": \"inject_anomaly\", \"magnitude\": 10, \"duration\": 30000}"
    echo "   - Watch temperature spike"
    echo "   - Alert appears in HMI"
    echo ""
    echo "4. SIMULATE DEFECT SPIKE"
    echo "   - Send to vision scanner:"
    echo "     {\"action\": \"inject_high_defects\", \"rate\": 0.2}"
    echo "   - Watch reject count climb"
    echo "   - Quality alert triggers"
    echo ""
    echo "5. TEST EMERGENCY STOP"
    echo "   - Press ENTER in E-Stop window"
    echo "   - All devices receive emergency broadcast"
    echo "   - Conveyor stops immediately"
    echo "   - Type 'reset' to clear"
    echo ""
    echo "6. DEMONSTRATE RECONNECTION"
    echo "   - Kill the conveyor process (Ctrl+C)"
    echo "   - Wait 10 seconds"
    echo "   - Restart conveyor"
    echo "   - Watch it receive last known state"
    echo ""
    echo "7. GATEWAY RESTART TEST"
    echo "   - Stop the gateway"
    echo "   - Watch devices reconnect"
    echo "   - Start gateway"
    echo "   - Consumers resume from JetStream"
    echo ""
    echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
}

# Main
print_banner

if [ "$1" == "--help" ] || [ "$1" == "-h" ]; then
    echo "Usage: $0 [option]"
    echo ""
    echo "Options:"
    echo "  --all     Start all devices immediately"
    echo "  --hmi     Start HMI only"
    echo "  --help    Show this help"
    echo ""
    exit 0
fi

if [ "$1" == "--all" ]; then
    check_prerequisites
    start_devices
    exit 0
fi

if [ "$1" == "--hmi" ]; then
    check_prerequisites
    open_terminal "HMI" "$BUILD_DIR/hmi_panel"
    exit 0
fi

check_prerequisites
show_menu
