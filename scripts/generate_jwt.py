#!/usr/bin/env python3
"""
Interactive JWT Token Generator for NATS WebSocket Bridge

Generates JWT tokens compatible with the gateway's authentication system.
Tokens can be used with:
  - Header-based auth: wscat -c ws://localhost:5000/ws -H "Authorization: Bearer <token>"
  - In-band auth: {"type":8,"payload":{"token":"<token>"}}

Requirements:
  pip install pyjwt

Usage:
  python generate_jwt.py              # Interactive mode
  python generate_jwt.py --help       # Show all options
  python generate_jwt.py -c sensor-01 # Quick generate with defaults
"""

import argparse
import json
import sys
from datetime import datetime, timezone, timedelta

try:
    import jwt
except ImportError:
    print("Error: PyJWT is required. Install with: pip install pyjwt")
    sys.exit(1)


# Default values matching appsettings.json
DEFAULT_SECRET = "CHANGE_THIS_TO_A_SECURE_SECRET_KEY_AT_LEAST_32_CHARS"
DEFAULT_ISSUER = "nats-websocket-bridge"
DEFAULT_AUDIENCE = "nats-devices"
DEFAULT_EXPIRY_HOURS = 24

# Common role presets
ROLE_PRESETS = {
    "sensor": {
        "description": "Sensor device - publish telemetry, subscribe to commands",
        "publish": ["telemetry.{clientId}.>", "factory.>"],
        "subscribe": ["commands.{clientId}.>"],
    },
    "actuator": {
        "description": "Actuator device - publish status, subscribe to commands",
        "publish": ["status.{clientId}.>", "events.>"],
        "subscribe": ["commands.{clientId}.>"],
    },
    "admin": {
        "description": "Admin device - full access to all topics",
        "publish": [">"],
        "subscribe": [">"],
    },
    "monitor": {
        "description": "Monitor device - subscribe only, no publish",
        "publish": [],
        "subscribe": [">"],
    },
    "custom": {
        "description": "Custom permissions - specify manually",
        "publish": [],
        "subscribe": [],
    },
}


def generate_token(
    client_id: str,
    role: str,
    publish: list[str],
    subscribe: list[str],
    expiry_hours: float,
    secret: str,
    issuer: str,
    audience: str,
) -> tuple[str, dict]:
    """Generate a JWT token with the given claims."""
    now = datetime.now(timezone.utc)
    exp = now + timedelta(hours=expiry_hours)

    payload = {
        "sub": client_id,
        "role": role,
        "pub": publish,
        "subscribe": subscribe,
        "iss": issuer,
        "aud": audience,
        "iat": int(now.timestamp()),
        "exp": int(exp.timestamp()),
    }

    token = jwt.encode(payload, secret, algorithm="HS256")
    return token, payload


def expand_patterns(patterns: list[str], client_id: str) -> list[str]:
    """Replace {clientId} placeholder with actual client ID."""
    return [p.replace("{clientId}", client_id) for p in patterns]


def interactive_mode(args):
    """Run in interactive mode, prompting for all values."""
    print("\n=== NATS WebSocket Bridge JWT Generator ===\n")

    # Client ID
    client_id = input(f"Client ID [{args.client_id or 'device-001'}]: ").strip()
    if not client_id:
        client_id = args.client_id or "device-001"

    # Role selection
    print("\nAvailable roles:")
    for i, (role, info) in enumerate(ROLE_PRESETS.items(), 1):
        print(f"  {i}. {role}: {info['description']}")

    role_input = input(f"\nSelect role [1-{len(ROLE_PRESETS)}] or enter custom: ").strip()

    if role_input.isdigit() and 1 <= int(role_input) <= len(ROLE_PRESETS):
        role = list(ROLE_PRESETS.keys())[int(role_input) - 1]
    elif role_input in ROLE_PRESETS:
        role = role_input
    elif role_input:
        role = role_input
    else:
        role = "sensor"

    # Get preset permissions or custom
    preset = ROLE_PRESETS.get(role, ROLE_PRESETS["custom"])

    if role == "custom" or role not in ROLE_PRESETS:
        print("\nEnter publish patterns (comma-separated, empty for none):")
        print("  Examples: telemetry.>, factory.line1.*, devices.sensor-01.data")
        pub_input = input("Publish patterns: ").strip()
        publish = [p.strip() for p in pub_input.split(",") if p.strip()] if pub_input else []

        print("\nEnter subscribe patterns (comma-separated, empty for none):")
        sub_input = input("Subscribe patterns: ").strip()
        subscribe = [p.strip() for p in sub_input.split(",") if p.strip()] if sub_input else []
    else:
        publish = expand_patterns(preset["publish"], client_id)
        subscribe = expand_patterns(preset["subscribe"], client_id)

        print(f"\nUsing {role} preset permissions:")
        print(f"  Publish: {publish}")
        print(f"  Subscribe: {subscribe}")

        customize = input("\nCustomize permissions? [y/N]: ").strip().lower()
        if customize == "y":
            pub_input = input(f"Publish patterns [{', '.join(publish)}]: ").strip()
            if pub_input:
                publish = [p.strip() for p in pub_input.split(",") if p.strip()]

            sub_input = input(f"Subscribe patterns [{', '.join(subscribe)}]: ").strip()
            if sub_input:
                subscribe = [p.strip() for p in sub_input.split(",") if p.strip()]

    # Expiry
    expiry_input = input(f"\nExpiry hours [{args.expiry_hours}]: ").strip()
    expiry_hours = float(expiry_input) if expiry_input else args.expiry_hours

    # Secret
    if args.secret == DEFAULT_SECRET:
        print(f"\nUsing default secret (for development only)")
    secret = args.secret

    # Generate token
    token, payload = generate_token(
        client_id=client_id,
        role=role,
        publish=publish,
        subscribe=subscribe,
        expiry_hours=expiry_hours,
        secret=secret,
        issuer=args.issuer,
        audience=args.audience,
    )

    print_token_output(token, payload, args)


def quick_mode(args):
    """Generate token directly from command-line arguments."""
    role = args.role or "sensor"
    preset = ROLE_PRESETS.get(role, ROLE_PRESETS["custom"])

    if args.publish:
        publish = args.publish
    else:
        publish = expand_patterns(preset["publish"], args.client_id)

    if args.subscribe:
        subscribe = args.subscribe
    else:
        subscribe = expand_patterns(preset["subscribe"], args.client_id)

    token, payload = generate_token(
        client_id=args.client_id,
        role=role,
        publish=publish,
        subscribe=subscribe,
        expiry_hours=args.expiry_hours,
        secret=args.secret,
        issuer=args.issuer,
        audience=args.audience,
    )

    print_token_output(token, payload, args)


def print_token_output(token: str, payload: dict, args):
    """Print the generated token and usage examples."""
    exp_time = datetime.fromtimestamp(payload["exp"], tz=timezone.utc)

    print("\n" + "=" * 60)
    print("Generated JWT Token")
    print("=" * 60)

    if args.format == "token":
        print(token)
    elif args.format == "json":
        print(json.dumps({"token": token, "payload": payload}, indent=2, default=str))
    else:
        print(f"\nToken:\n{token}\n")
        print(f"Payload:\n{json.dumps(payload, indent=2)}\n")
        print(f"Expires: {exp_time.isoformat()}")
        print(f"         ({payload['exp']} Unix timestamp)")

        print("\n" + "-" * 60)
        print("Usage Examples")
        print("-" * 60)

        print("\n1. Header-based auth (recommended for CLI tools):")
        print(f'   wscat -c ws://localhost:5000/ws -H "Authorization: Bearer {token}"')

        print("\n2. In-band auth (for browsers):")
        print(f'   wscat -c ws://localhost:5000/ws')
        print(f'   # Then send:')
        print(f'   {{"type":8,"payload":{{"token":"{token}"}}}}')

        print("\n3. Environment variable:")
        print(f"   export GATEWAY_TOKEN='{token}'")
        print(f'   wscat -c ws://localhost:5000/ws -H "Authorization: Bearer $GATEWAY_TOKEN"')

        print("\n4. Test with curl (check /dev/token endpoint):")
        print(f"   curl -s http://localhost:5000/devices")


def main():
    parser = argparse.ArgumentParser(
        description="Generate JWT tokens for NATS WebSocket Bridge authentication",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s                           # Interactive mode
  %(prog)s -c sensor-01              # Quick generate with sensor role
  %(prog)s -c admin-01 -r admin      # Admin with full access
  %(prog)s -c custom-01 -r custom -p "telemetry.>" -s "commands.>"
  %(prog)s -c test -e 0.5            # 30-minute expiry
  %(prog)s -c prod-sensor --secret "your-production-secret"

Role Presets:
  sensor   - Publish to telemetry/factory, subscribe to commands
  actuator - Publish to status/events, subscribe to commands
  admin    - Full access to all topics (>)
  monitor  - Subscribe only, no publish
  custom   - Specify permissions manually with -p and -s
        """,
    )

    parser.add_argument(
        "-c", "--client-id",
        help="Device/client identifier (JWT 'sub' claim)",
    )
    parser.add_argument(
        "-r", "--role",
        choices=list(ROLE_PRESETS.keys()),
        help="Device role (determines default permissions)",
    )
    parser.add_argument(
        "-p", "--publish",
        nargs="*",
        help="Publish permission patterns (overrides role preset)",
    )
    parser.add_argument(
        "-s", "--subscribe",
        nargs="*",
        help="Subscribe permission patterns (overrides role preset)",
    )
    parser.add_argument(
        "-e", "--expiry-hours",
        type=float,
        default=DEFAULT_EXPIRY_HOURS,
        help=f"Token expiry in hours (default: {DEFAULT_EXPIRY_HOURS})",
    )
    parser.add_argument(
        "--secret",
        default=DEFAULT_SECRET,
        help="JWT signing secret (default: development secret from appsettings.json)",
    )
    parser.add_argument(
        "--issuer",
        default=DEFAULT_ISSUER,
        help=f"JWT issuer claim (default: {DEFAULT_ISSUER})",
    )
    parser.add_argument(
        "--audience",
        default=DEFAULT_AUDIENCE,
        help=f"JWT audience claim (default: {DEFAULT_AUDIENCE})",
    )
    parser.add_argument(
        "-f", "--format",
        choices=["full", "token", "json"],
        default="full",
        help="Output format: full (default), token (just the token), json",
    )
    parser.add_argument(
        "-i", "--interactive",
        action="store_true",
        help="Force interactive mode even with other arguments",
    )

    args = parser.parse_args()

    # Determine mode
    if args.interactive or not args.client_id:
        interactive_mode(args)
    else:
        quick_mode(args)


if __name__ == "__main__":
    main()
