# AppHost

A local launcher that starts MockEntra, WebhookReceiver and AgentServer, and shows their traces, metrics and logs in a dashboard. Back to the [README](../../README.md).

```bash
dotnet run --project samples/AppHost
```

The only prerequisite is the .NET 10 SDK. No Docker, no Aspire CLI, no workload. The apps run as normal processes.

1. Open the `http://localhost:18888/login?t=...` link that the command prints.
2. On the Resources page, open the "..." menu of `agentclient` and click Start. It runs all 13 flows.
3. See one trace (named `agentclient: flow <name>`) per flow, the A2A metrics, and logs linked to traces.

The dashboard shows a red banner, "No trusted development certificate". It is harmless: the AppHost uses plain HTTP on localhost. `dotnet dev-certs https --trust` removes it if you want (optional).

The apps keep their usual ports (MockEntra 5100, AgentServer 5200 with gRPC on 5201 to 5204, WebhookReceiver 5300). The AppHost puts no proxy in front of them.

## What it is for

Looking at telemetry on your own machine. Nothing else.

## What it is not

- Not part of any Docker image or the docker-compose file.
- Not for production.
- Not needed on AKS. There, point the apps at your OpenTelemetry Collector; see [docs/production.md](../../docs/production.md#6-observability-otlp).

## Your data

- The dashboard listens on localhost only and keeps data in memory. It is gone when you stop the AppHost.
- The dashboard login token stays on.
- Usage telemetry opt-outs are set in `Properties/launchSettings.json`: `ASPIRE_DASHBOARD_TELEMETRY_OPTOUT=true` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Microsoft says dashboard usage telemetry is only sent when the dashboard starts from Visual Studio or VS Code. Microsoft's docs do not say whether the dashboard opt-out also stops unhandled-exception reports, so do not assume it does.

## AgentClient from a separate terminal

It does not send to the dashboard. The dashboard needs an OTLP API key that only apps started by the AppHost get, and it answers 401 otherwise. Start `agentclient` from the dashboard.
