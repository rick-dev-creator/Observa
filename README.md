# Observa

Self-hosted financial observability. A focused dashboard for personal income and outcome streams that answers three questions:

1. **Where am I?** — net flow for the current period, broken down by stream and category.
2. **Where am I going?** — rolling averages and per-stream trend.
3. **Where will I be?** — projected next period based on scheduled flows plus historical variance.

Observa is intentionally macro: it tracks the *streams* (salary, Patreon, subscriptions, housing, etc.), not every individual transaction. Detail-level budgeting is out of scope — that is what a budgeting app is for. Observa is the dashboard above it.

## Status

Skeleton. Solution scaffolded, persistence and feature slices are next.

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# 14 (LangVersion=preview) |
| UI | Blazor Server |
| Styling | Tailwind CSS v4 |
| Domain | [Crucible](https://github.com/rick-dev-creator/crucible) — DDD framework with source generators |
| State / persistence | Orleans 9.x with PostgreSQL provider (grain storage + reminders) |
| Orchestration (dev) | .NET Aspire 13 (AppHost manages Postgres container + Web) |
| Currency | USD only (no multi-currency in this version) |

The chain pattern of Crucible is the dispatcher; there is no MediatR or equivalent. Each request flows `Blazor → Service → Grain → Aggregate (Crucible chain) → Response`. Connectors (Stripe, Patreon, bank, etc.) live in their own slice and are called from the service layer, never from inside a grain.

## Architecture

Vertical slices. Crucible enforces the domain shape at compile time (private constructors, factory methods, typestate composition, 27 build-time diagnostics), so the codebase does not need horizontal layering (Domain/Application/Infrastructure). Each feature owns its aggregate, grain, service, events, handlers, and pages.

```
src/
├── Observa.AppHost/                 Aspire orchestrator: Postgres + Web
├── Observa.ServiceDefaults/         Shared service config: OTel, health checks
└── Observa.Web/                     Blazor Server + Orleans silo (co-hosted)
    ├── Program.cs
    ├── Styles/app.tailwind.css      Tailwind input (compiled to wwwroot/app.css)
    ├── package.json                 Tailwind CLI dependency
    └── Features/                    Vertical slices (added per feature)
        ├── Streams/                 The Stream aggregate slice (planned)
        ├── Reporting/               Queries — outside Crucible scope (planned)
        └── Connectors/              Plugin pattern for external sources (planned)
```

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (for the Tailwind CLI invoked during build)
- Docker or Podman (for the Aspire-managed PostgreSQL container)
- [Crucible](https://github.com/rick-dev-creator/crucible) cloned as a sibling directory:

```
~/Projects/
├── crucible/        <-- required sibling
└── Observa/        <-- this repo
```

While Crucible is pre-1.0 it is consumed via `ProjectReference`. Once published to NuGet the references will switch to `PackageReference`.

## Building

```bash
git clone https://github.com/rick-dev-creator/Observa.git
cd Observa
dotnet build
```

The build target invokes `npm install` and the Tailwind CLI automatically — no manual CSS step required.

## Running

```bash
dotnet run --project src/Observa.AppHost
```

Aspire starts the Postgres container, applies any pending schema, and launches the Blazor app. The Aspire dashboard opens in the browser with links to the running services and to PgAdmin.

For UI development with hot CSS reload, run the Tailwind watcher in a second terminal:

```bash
cd src/Observa.Web
npm run css:watch
```

## License

MIT
