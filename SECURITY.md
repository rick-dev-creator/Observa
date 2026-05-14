# Security policy

## Supported versions

Observa is pre-1.0. Security fixes are applied to `main` only. There are no LTS branches.

## Reporting a vulnerability

**Please do not open a public issue for security reports.**

If you discover a vulnerability, report it privately through GitHub's [private vulnerability reporting](https://github.com/rick-dev-creator/Observa/security/advisories/new) flow. You will receive an acknowledgement within a few business days. Coordinated disclosure is preferred — please give a reasonable window (typically 30 days) before public disclosure so a patch can be prepared.

When reporting, include:

- A clear description of the vulnerability and its impact.
- Steps to reproduce, ideally with a minimal proof-of-concept.
- The version (commit SHA) you tested against.
- Any suggestions for remediation, if you have them.

## Threat model

Observa is **self-hosted, single-user by design**. It does not implement authentication and is intended to run on a private network (LAN, Tailscale, Cloudflare Tunnel) or behind an auth proxy (Cloudflare Access, Authelia, oauth2-proxy). Exposing Observa directly to the public internet is **not a supported deployment**.

What is in scope for security reports:

- Remote code execution, SQL injection, deserialization issues in code Observa controls.
- Information disclosure through API responses or static assets.
- Vulnerabilities in the Patreon connector or other ingestion paths that could be triggered by a malicious upstream response.
- Container hardening issues in the published Dockerfile.

What is **out of scope**:

- Lack of authentication (this is intentional — see the security model in the README).
- Issues that require physical access to the host or compromise of the proxy layer above Observa.
- Vulnerabilities in third-party dependencies that have not yet been disclosed upstream (file an issue upstream first).

## Dependencies

[Crucible](https://github.com/rick-dev-creator/crucible), Orleans, Blazor, ApexCharts, and PostgreSQL are tracked via Dependabot. Patch-level dependency updates are merged automatically when CI passes.
