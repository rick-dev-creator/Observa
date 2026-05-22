# Observa

![Observa dashboard](docs/dashboard.png)

Self-hosted financial observability. A focused dashboard for personal income and outcome streams that answers three questions:

1. **Where have I been?** — year-to-date net, year-over-year delta, best and worst months, cumulative running balance.
2. **Where am I?** — net flow for the current month, broken down by stream and category.
3. **Where am I going?** — projected end-of-month, three-month, and year-end net, plus per-stream trend and runway.

Observa is intentionally macro: it tracks the *streams* (salary, Patreon, subscriptions, housing, etc.), not every individual transaction. Detail-level budgeting is out of scope — that is what a budgeting app is for. Observa is the dashboard above it.

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# 14 (LangVersion=preview) |
| UI | Blazor Server + Tailwind CSS v4 |
| Charts | [Blazor-ApexCharts](https://github.com/joadan/Blazor-ApexCharts) |
| Domain | [Crucible](https://github.com/rick-dev-creator/crucible) — DDD framework with source generators |
| State / persistence | Orleans 9.x with PostgreSQL provider (grain storage + reminders) |
| Orchestration (dev) | .NET Aspire 13 (AppHost manages Postgres + Web) |
| Orchestration (prod) | Docker Compose |
| Currency | USD only (no multi-currency in this version) |

The chain pattern of Crucible is the dispatcher; there is no MediatR or equivalent. Each request flows `Blazor → Service → Crucible chain → Aggregate → Handler → Orleans grain → Response`. Connectors (Patreon and BloFin today; Stripe, bank, CSV as next slices) live in their own feature and are called from the orchestrator, never from inside a grain.

## Architecture

Vertical slices. Crucible enforces the domain shape at compile time (private constructors, factory methods, typestate composition, 27 build-time diagnostics), so the codebase does not need horizontal layering (Domain/Application/Infrastructure). Each feature owns its aggregate, grain, service, events, handlers, and pages.

```
src/
├── Observa.AppHost/                 Aspire orchestrator: Postgres + Web (dev only)
├── Observa.ServiceDefaults/         Shared service config: OTel, health checks
├── Observa.Connectors.Abstractions/ IConnector + ConnectorFlowEvent contract
├── Observa.Connectors.Patreon/      Patreon API v2 connector with historical backfill
├── Observa.Connectors.Blofin/       BloFin affiliate connector: daily commission as income events
└── Observa.Web/                     Blazor Server + Orleans silo (co-hosted)
    ├── Program.cs
    ├── Dockerfile                   Multi-stage build (.NET SDK + Node for Tailwind)
    ├── Styles/app.tailwind.css      Tailwind input
    └── Features/                    Vertical slices
        ├── Streams/                 Stream aggregate, grain, services, pages
        └── Connectors/              Manual, Recurring, plus registry + orchestrator
```

## Self-hosted deployment

Observa ships with a `compose.yml` for production-style deployment.

### Prerequisites

- Docker 25+ with the Compose plugin
- A Patreon Creator Access Token if you want to connect a Patreon campaign (optional)
- BloFin affiliate API credentials (key + secret + passphrase) if you want to track affiliate rebates (optional)

### First-time setup

```bash
git clone https://github.com/rick-dev-creator/Observa.git
cd Observa

cp .env.example .env
# Edit .env and set at minimum:
#   POSTGRES_PASSWORD       any value, used to initialise the Postgres volume
#   PATREON_ACCESS_TOKEN    your Patreon Creator Access Token (optional)
#   BLOFIN_API_KEY / BLOFIN_SECRET_KEY / BLOFIN_PASSPHRASE  BloFin affiliate API (optional)

docker compose up -d --build
```

Open `http://localhost:8080` (or whatever `WEB_PORT` you set). The seed data only runs in `Development`; the container starts in `Production`, so the first screen is the empty-state asking you to register your first stream.

### Day-to-day commands

```bash
docker compose logs -f web      # follow web logs
docker compose down             # stop, keep the postgres volume
docker compose down -v          # stop and erase data
docker compose up -d --build    # rebuild image and restart
```

### Security model

Observa runs **without authentication**. It assumes deployment on a private network (LAN, Tailscale, Cloudflare Tunnel) or behind an auth proxy (Cloudflare Access, Authelia, oauth2-proxy). **Do not expose Observa directly to the public internet** — your financial history would be world-readable.

The pattern is intentional: the deployment context belongs to whoever runs Observa, and they will know better than the app whether HTTPS, MFA, geo-restrictions, or device posture checks are appropriate. The app does not duplicate that layer.

## Local development

Aspire orchestrates Postgres + Web for development, including a synthetic seed (15 streams, ~3 years of monthly events) so the UI is never empty when you start working.

### Prerequisites

- .NET 10 SDK
- Node.js 20+ (for the Tailwind CLI invoked during build)
- Docker or Podman (for the Aspire-managed PostgreSQL container)

[Crucible](https://github.com/rick-dev-creator/crucible) is consumed via NuGet (`Crucible.Domain`, `Crucible.Chains`, `Crucible.Generators` at v2.2.0) — no sibling clone required.

### Running

```bash
git clone https://github.com/rick-dev-creator/Observa.git
cd Observa
dotnet run --project src/Observa.AppHost
```

The Aspire dashboard opens in the browser with links to the running services and to PgAdmin.

For UI development with hot CSS reload, run the Tailwind watcher in a second terminal:

```bash
cd src/Observa.Web
npm run css:watch
```

## Roadmap

What's shipped, what's queued next, and what is deliberately deferred lives in [ROADMAP.md](ROADMAP.md).

## Authorship

Design, architecture, and direction by [rick-dev-creator](https://github.com/rick-dev-creator). Code written by Claude (Anthropic) under the constraints of [Crucible](https://github.com/rick-dev-creator/crucible) and an explicit per-iteration review loop.

If you are interested in how the human-AI collaboration was actually structured — the framework-level compile-time constraints, the additional rules imposed by the owner, and what that produced versus an unconstrained LLM — see [docs/AUTHORSHIP.md](docs/AUTHORSHIP.md).

## License

[MIT](LICENSE).
