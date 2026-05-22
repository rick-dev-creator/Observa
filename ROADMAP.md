# Roadmap

Observa is intentionally small and macro. This file tracks what is shipped, what is queued next, and ideas that are deliberately deferred or out of scope. The order inside each section is not a strict commitment — priorities shift as the app gets used.

## Shipped

- Stream aggregate with full lifecycle (Register, Pause, Resume, Stop, Delete) and Crucible-enforced typestate.
- Orleans grain persistence on Postgres with reminders.
- Connectors: Manual (placeholder), Recurring (schedule-driven backfill via `StartFrom`), Patreon (real v2 API with historical reconstruction from `lifetime_support_cents`), BloFin (real affiliate API — daily commission across all invitees aggregated into one income event per day, signed with API key/secret/passphrase).
- Dashboard tabs: Overview (KPIs, retrospective, running balance, yearly trend, monthly per-stream, streams table) and What-if (preset scenarios + comparison cards + line chart + per-stream impact).
- Stream multiselect filter on Overview, applied to every chart and KPI.
- Stacked bar charts per stream (yearly + monthly) with overlaid Net line.
- Modal-based stream registration; sidebar animated drawer; mobile-first layout.
- Docker Compose deployment + GHCR image published from CI; ZimaOS / Casa OS app metadata.
- OSS hygiene: CI workflow, Docker image workflow, Dependabot, issue and PR templates, SECURITY.md, CONTRIBUTING.md, gitattributes.
- Authorship deep-dive in [docs/AUTHORSHIP.md](docs/AUTHORSHIP.md) describing how Crucible's compile-time enforcement reshaped what the LLM wrote.

## Next up

### CSV import on the stream detail page

For tracking variable expenses (credit cards, bank statements) that arrive as a monthly CSV.

- **Trigger**: an "Import CSV" button on `/streams/{id}` for streams without an automated connector (Manual or no binding).
- **Flow**: file upload → preview of first 5 rows → user picks the date and amount columns from the parsed headers → server iterates rows and ingests each via `StreamService.IngestEventAsync`.
- **Idempotency**: `ExternalRef = "csv-{date:yyyyMMdd}-{amount:F2}-{rowIndex}"` so re-importing the same file dedupes through the aggregate's existing check.
- **Format flexibility**: any CSV with a header row works — the importer never assumes a specific bank. Toggle for "values are negative" handles banks that sign expenses as negative; the parser tries comma-decimal and dot-decimal.
- **Optional "Aggregate to daily total" checkbox**: collapse all rows of the same day into one event before ingestion, so a transaction-per-row export yields a clean daily series instead of a noisy spike per purchase.
- **Out of scope**: categorization by description, bank-specific parsers, automatic re-imports. The point is the trend, not the line items.

### Opening balance on the running balance chart

The cumulative balance currently assumes zero at the first event. Letting the user set an opening balance (one number per deployment, stored on a singleton grain) turns the chart from "net change since tracking started" into actual savings trajectory.

### Concentration / risk indicator on Overview

A small card showing "top stream provides X% of income" with thresholds (green <50%, amber 50–70%, red >70%). One number, one signal: how exposed are you to losing a single income source.

## Considered, deferred

These are tracked so they aren't re-proposed; some may move up if there is a concrete use case.

### Custom scenario builder (What-if "fine-tune" panel)

A collapsible section under the preset chips with per-stream toggles plus a multiplier slider (0%–200%) per stream, so a user can construct an arbitrary scenario instead of picking from presets. Useful once the preset list stops covering common questions; for now the presets adapt to the user's actual streams and cover the obvious cases.

### Schedule-aware projection

The current projection mixes scheduled-and-fixed streams with irregular ones in a single linear regression on monthly net. A hybrid model would use each stream's `Recurrence` + `ExpectedAmount` deterministically for the predictable streams and statistics only for the rest. Higher accuracy on the bills-and-salary case where the math is just "day X of month Y minus rent on day Z".

### Real percentile bands on projections

Replace the `±stddev` shorthand with p50 / p90 computed from the historical distribution of monthly nets. Honest when the distribution is fat-tailed.

### Anomaly flags per stream

On the streams table, mark with a badge any stream whose latest month deviates more than ~2σ from its own historical mean. "Patreon down 40% this month" auto-surfaced rather than discovered by squinting at the sparkline.

### Connector: bank (Plaid / GoCardless)

By far the biggest leverage — one connector that covers every account and card. Significant scope (OAuth flow, item refresh, transaction normalization, multi-currency conversion) and per-month cost. On the table once CSV import has been used long enough to justify the upgrade.

### Connector: Stripe

Pattern is essentially the same as Patreon (creator access token, list of payments, historical backfill). Cheap to add once needed.

### Connector: PayPal

Same shape. Relevant for freelancers and creators.

### Saved scenarios

Persist named scenarios ("Plan A: raise", "Plan B: lose Patreon", "Plan C: both") on a grain so they survive reloads and can be compared side by side. Worth doing if the What-if tab gets used frequently for real decisions; not before.

### Goals and budgets

A target ("$X saved by Y date") with a progress bar, and per-category outcome caps with alerts when run-rate is on track to exceed them. Edges toward "budgeting app" territory — the explicit non-goal of Observa — so this stays deferred unless a strong use case appears.

### Multi-currency

USD-only is a hard constraint in this version. Adding currency conversion requires picking an exchange-rate source, deciding whether to store events in original or normalized currency, and recomputing historical aggregates when rates change. Worth doing if the user holds meaningful balances in non-USD accounts and the JPY salary stays in JPY in their head, not in the dashboard.

## Out of scope (closed)

- **Per-transaction budgeting / categorization within a stream.** Observa is the dashboard *above* budgeting apps. The Manual + CSV path captures totals; the line-items live in the budgeting tool of choice (ezbookkeeping, Actual, YNAB, etc.).
- **Authentication in the app.** Deployments belong on a private network or behind an auth proxy. See the Security model section of the README.
- **Mobile native app.** The Blazor UI is responsive and reachable from any browser on the LAN / VPN; that is the contract.
