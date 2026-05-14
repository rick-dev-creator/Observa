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

The chain pattern of Crucible is the dispatcher; there is no MediatR or equivalent. Each request flows `Blazor → Service → Crucible chain → Aggregate → Handler → Orleans grain → Response`. Connectors (Patreon today; Stripe, bank, CSV as next slices) live in their own feature and are called from the orchestrator, never from inside a grain.

## Architecture

Vertical slices. Crucible enforces the domain shape at compile time (private constructors, factory methods, typestate composition, 27 build-time diagnostics), so the codebase does not need horizontal layering (Domain/Application/Infrastructure). Each feature owns its aggregate, grain, service, events, handlers, and pages.

```
src/
├── Observa.AppHost/                 Aspire orchestrator: Postgres + Web (dev only)
├── Observa.ServiceDefaults/         Shared service config: OTel, health checks
├── Observa.Connectors.Abstractions/ IConnector + ConnectorFlowEvent contract
├── Observa.Connectors.Patreon/      Patreon API v2 connector with historical backfill
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

### First-time setup

```bash
git clone https://github.com/rick-dev-creator/Observa.git
cd Observa

cp .env.example .env
# Edit .env and set at minimum:
#   POSTGRES_PASSWORD       any value, used to initialise the Postgres volume
#   PATREON_ACCESS_TOKEN    your Patreon Creator Access Token (optional)

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

## Authorship

**Design, architecture, and direction** — [rick-dev-creator](https://github.com/rick-dev-creator).
**Code** — written by Claude (Anthropic), under the explicit constraints below.

### How this codebase was built

Observa was built across a series of iterative pairing sessions. The owner makes every architectural decision: what the domain looks like, where boundaries fall, what the dashboard should answer, which features ship, which trade-offs are acceptable. The LLM writes the C#, Razor, Dockerfiles, and Tailwind that implements those decisions, runs the tests, verifies behavior, and shows the results back. The owner reviews diffs before every push.

### Constraints the owner imposed on the LLM

These are the guardrails. They exist because LLMs reliably drift toward verbose, defensive, over-abstracted code if left unchecked.

- **Crucible enforces the domain shape at compile time.** Private constructors, factory methods, typestate composition, and 27 build-time diagnostics block the AI-introduced anti-patterns this project's owner has seen in real codebases (anemic models, public mutation, free-form error strings, validation bypassed by infrastructure). The compiler catches what review cannot catch every time.
- **No prose comments.** Code is read more often than it is written; well-named identifiers explain *what*. Comments are reserved for non-obvious *why* — a hidden constraint, a workaround, a subtle invariant. The LLM is not allowed to narrate its own code.
- **No premature abstractions.** Three similar lines is better than a wrong abstraction. No helpers, no interfaces, no "extensibility hooks" until a second concrete caller exists. A bug fix doesn't need surrounding cleanup; a feature doesn't need a future-proofed plugin system.
- **No defensive validation at internal boundaries.** Trust the framework, trust the type system, trust the calling code. Validate only at system boundaries (user input, external APIs).
- **English in code, regardless of chat language.** The pairing happens in Spanish; identifiers, commit messages, and UI strings are English. No mixed-language artifacts.
- **Verify before claiming.** The LLM does not say "this works" without showing test output, a curl response, or a build log. When the owner spotted that one connector's reconstruction inflated a lifetime total against the real number, the fix was to validate the API math against the upstream ground-truth field rather than guess at the cause.
- **No auth in the app.** The LLM proposed three implementation paths; the owner picked "no auth, document the assumption" because the deployment context belongs to whoever runs Observa.
- **Real data over mocks.** Integration tests hit the real Patreon API (gated by env vars) so regressions in the upstream contract surface immediately. The seed service generates synthetic streams only in `Development` to support UI work, never in production.
- **Mobile-first.** Every page works on a phone before it ships. Tabs, modals, charts: all responsive without separate "mobile pages".
- **Small, focused commits with clear subject lines.** The LLM groups related changes into one commit and writes the message; the owner reviews the diff before push. No "WIP" commits, no force-pushing tags, no skipping pre-commit hooks.
- **Secrets never enter the repo.** API keys live in `.env` (gitignored) or `appsettings.Development.json` (also gitignored where relevant). Tokens shared during integration testing are rotated by the owner afterwards.

### Why this matters

Most LLM-generated codebases ship the LLM's defaults: defensive null checks, speculative abstractions, exception swallowing, redundant comments, English-Spanish drift, "WIP" commits. The result compiles but ages badly. The constraints above aren't novel — they're the discipline a strong senior reviewer would impose anyway — but encoding them in compile-time tooling (Crucible) plus a strict per-iteration review loop is what keeps the LLM from regressing toward those defaults. Net forward progress per session beats raw token throughput.

## License

[MIT](LICENSE).
