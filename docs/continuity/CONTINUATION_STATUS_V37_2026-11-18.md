# Atendee Beauty - Continuity Status V37 (2026-11-18)

## Repository and validation
- Branch: `security/csrf-webhook-antiforgery-v23`; `main` untouched. No deploy and no public signup.
- Commit: `7d681d5da2b8bf58bff8c4b1bd651983639b08ab` — Propagate trusted actor to database audit triggers.
- Working tree was clean after the code commit.
- GitHub Actions: [run 44223700861](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44223700861), completed `success`, exact tested SHA `7d681d5da2b8bf58bff8c4b1bd651983639b08ab`, PostgreSQL 18.7.
- CI steps observed successful: locked restore, Release build, 19 domain checks, isolated PostgreSQL 18.7 startup, restricted-role provisioning, EF Core/RLS checks, Identity/TOTP checks, and API HTTP checks.
- CI log explicitly contains the new assertions for stale session actor reset and server-resolved actor propagation despite a client-supplied `actorId`.

## V37 implementation
- Added migration `db/migrations/008_trusted_actor_for_trigger_audit.sql`.
- Added `identity.trusted_audit_actor()`: absent/empty setting yields NULL for maintenance paths; malformed and nil UUID values fail; unknown actor IDs fail through the existing foreign key.
- Replaced the membership and tenant-status trigger functions so trigger-written audit events read `app.actor_id` within the current transaction.
- Updated `NpgsqlTenantTransactionRunner` to clear any session-level `app.actor_id` on connection checkout and set it transaction-locally from `ICurrentActorContext.ActorId` after trusted tenant context validation. Actor and tenant values use bound parameters.
- Added persistence test proving a stale session-level actor cannot survive connection-pool reuse.
- Added HTTP/PostgreSQL integration coverage: client sends a forged `actorId`, but the membership trigger records the actor resolved from the authenticated server-side membership and the correct target exactly once.
- Added migration 008 to the PostgreSQL CI workflow. No new HTTP endpoint or administrative action was exposed.

## Security limitation
PostgreSQL custom settings are not cryptographically authenticated. A compromised application/runtime database session with permission to mutate audited tables could set `app.actor_id` itself. The guarantee is that application code obtains the actor only from the trusted server-side request context and never from client input; this is not protection against a compromised runtime role or DB owner/superuser. Do not describe the ledger as immutable against privileged database operators.

## Local checks
- `dotnet build Atendee.sln --no-restore --configuration Release`: success, 0 warnings, 0 errors.
- `Atendee.Domain.Checks`: 19/19 passed.
- PostgreSQL integration suites were validated in GitHub Actions, not on the local Windows machine.

## Next safe increment
Extend identity-target audit events with a trusted `TargetTenantId`, resolved from persisted membership within the transaction (never from request input), and add PostgreSQL tests for tenant/identity target consistency and grants. Do not expose an audit read/export endpoint or tenant-visible read view until the product owner explicitly decides audit visibility, retention, and post-Closed handling. Then continue the owner-decision items in `ADMIN_ACTIONS_DESIGN_V1.md`.

## Guardrails
No changes to `main`; no deploy; no public signup; no admin endpoints; no tenant-status policy changes; no client-supplied actor/tenant/role trust; no secrets or sensitive request data in audit metadata. CI success is not production approval.
