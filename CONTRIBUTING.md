# Contributing to Observa

Thank you for considering a contribution. Observa is a small, opinionated project — please read this short guide before opening a PR.

## Before you start

- **Open an issue first** for anything larger than a typo or a dependency bump. Observa has a deliberate scope (macro financial observability, not a budgeting app) and a deliberate code style (see [docs/AUTHORSHIP.md](docs/AUTHORSHIP.md)). A short discussion in an issue avoids wasted work on changes that won't be merged.
- **Check the security model.** If your change introduces authentication, exposes secrets, or alters the trust boundaries, mention it explicitly in the issue. The default assumption is "private deployment behind a proxy".

## Development setup

Local development uses .NET Aspire as the orchestrator:

```bash
git clone https://github.com/rick-dev-creator/Observa.git
cd Observa
dotnet run --project src/Observa.AppHost
```

The Aspire dashboard prints a URL with a login token. Postgres + the web app come up automatically. The dev seed populates 15 synthetic streams so the UI is never empty.

Requirements: .NET 10 SDK, Node 20+, Docker or Podman.

## Coding standards

These are not negotiable in PRs.

- **Crucible discipline applies.** Aggregates have private constructors, value objects construct via `Create`, domain errors are `Result<T>` values with codes from `DomainErrors`, every `[Step]` has a registered handler. The compiler will catch you; don't try to suppress diagnostics.
- **No prose comments.** Use comments only for non-obvious *why* (hidden constraints, workarounds, subtle invariants). The owner will ask you to remove explanatory comments.
- **No premature abstractions.** Three similar lines is better than a wrong abstraction. Don't add interfaces or extensibility hooks until a second concrete caller exists.
- **English in code.** Identifiers, comments, commit messages, and UI strings stay English regardless of the chat language.
- **Trust internal boundaries.** Validate only at system edges (user input, external APIs). Don't add defensive null-checks where the type system already prevents the issue.
- **Mobile-first.** Every page works on a phone before it ships.

See [docs/AUTHORSHIP.md](docs/AUTHORSHIP.md) for the longer rationale.

## Tests

- Run `dotnet test` from the repo root before opening a PR. CI runs the same.
- The Patreon integration tests are gated by `PATREON_ACCESS_TOKEN` and `PATREON_CAMPAIGN_ID` env vars. They skip silently when those are unset, so CI does not need credentials.
- New connectors should ship with integration tests that hit the real upstream API when credentials are available, plus unit-style tests for guard clauses.

## Commit and PR style

- One logical change per commit. Squash WIP commits before pushing.
- Commit subjects: imperative, ≤72 chars. Use a body when the *why* needs explaining.
- PR descriptions: a Summary section, plus a Test Plan (what you ran and what you saw).
- No force-pushing tags. No skipping pre-commit hooks (`--no-verify`).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE) that covers this project.
