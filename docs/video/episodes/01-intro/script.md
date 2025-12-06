# Episode 01: Narration Script

## [0:00] Hook

What happens when ten thousand sensors need to talk to your cloud? In real-time. With guaranteed delivery. And you can't afford to lose a single reading.

That's the challenge we're solving in this series.

## [0:30] The Manufacturing Challenge

I'm going to show you how we built a real-time device communication system for a pharmaceutical manufacturer. We call them PharmaCo.

They run smart packaging lines - think bottles, labels, vision scanners, weight sensors - all generating data every second. Each line has about 50 devices. They have 12 lines. That's 600 devices sending telemetry constantly.

Now here's what makes pharma special: FDA regulations. Every temperature reading, every batch event, every alert - it all needs to be recorded, timestamped, and auditable. You can't lose data. You can't have gaps in your records.

And factory networks? They're not your pristine data center. Equipment gets moved. Wi-Fi drops. Ethernet cables get unplugged. Your system needs to handle all of that gracefully.

## [2:30] Traditional Approaches

So what do most people try first?

HTTP polling. Have your devices call an API every few seconds. Simple, right? But the latency adds up. You're wasting bandwidth asking "any updates?" over and over. And you can't push messages down to devices easily.

MQTT is better - it's designed for IoT. Pub/sub model works great. But clustering MQTT brokers? That's where it gets complex. And persistence usually means bolting on another database.

Then there's Kafka. Incredibly powerful, battle-tested at scale. But it's heavy. Running Kafka at the edge? Managing ZooKeeper? For a factory with a small IT team, that's a lot of operational burden.

## [4:00] Enter NATS

This is where NATS comes in.

NATS is a single binary. About 20 megabytes. No external dependencies. You download it, you run it, you're done.

It's designed for cloud-native and edge deployments. The same server runs in your Kubernetes cluster and on a Raspberry Pi at the factory.

With JetStream, you get persistence. Messages are stored. Consumers can replay from any point. If a device reconnects after an outage, it catches up on what it missed.

And the performance? We're talking millions of messages per second. For our use case, it's absolute overkill in the best way.

## [6:00] System Architecture

Let me walk you through what we built.

On the left, you have devices. PLCs, sensors, vision systems. They speak different protocols, but they all connect to our gateway over WebSockets.

The gateway is a C# service. It handles authentication, message validation, rate limiting. It's the gatekeeper.

Behind the gateway sits NATS with JetStream. Messages flow into streams organized by subject. Telemetry goes to the TELEMETRY stream. Events go to EVENTS. Alerts to ALERTS.

Then we have consumers. The Historian service pulls from these streams and writes to TimescaleDB for long-term storage. Other services can subscribe for real-time processing.

And wrapping everything: Prometheus for metrics, Grafana for dashboards, Loki for logs. Full observability.

## [8:00] What We'll Build

Over the next six episodes, we're going to build this entire system together.

Episode 2 covers NATS fundamentals - subjects, pub/sub, JetStream streams and consumers.

Episode 3 dives into the C# gateway - how we handle thousands of WebSocket connections efficiently.

Episode 4 is about the WebSocket protocol itself - authentication flows, message formats, error handling.

Episode 5 builds the C++ device SDK that runs on embedded systems.

Episode 6 sets up monitoring - Prometheus metrics, Grafana dashboards, alerting.

And Episode 7 tackles historical data retention - TimescaleDB, compliance requirements, cold storage archival.

## [9:00] Closing

The code is all open source. Link in the description.

If you're building IoT systems, device communication platforms, or just want to learn NATS in a real-world context - subscribe and follow along.

I'll see you in Episode 2 where we dive into NATS fundamentals.
