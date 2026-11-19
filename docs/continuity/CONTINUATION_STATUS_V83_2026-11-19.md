# Atendee Beauty — Continuity Status V83 (approved, 2026-11-19)

## Result

V83 introduces the first operational customer-management vertical slice, including tenant-scoped domain, persistence, HTTP endpoints, PostgreSQL migration, role/status enforcement, pagination and audit. The implementation and follow-up analyzer correction were validated by the complete GitHub Actions workflow against exact head `2a4a1aff0b3a53b34167981fd894092bb0b8f7a3`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44587416282)
- Run: `44587416282`, conclusion `success`.
- Implementation commit: `6ba155281e157d7b17a95646d4b2ccdd927ab49a`.
- Follow-up test-safety commit: `2a4a1aff0b3a53b34167981fd894092bb0b8f7a3` (parameterized EF Core audit queries; final validated head).
- No production deployment, production data change, or external provider activation.

## Scope V83 — tenant customer management

Added:

- Domain `Customer` with required display name, at least one contact method, E.164 phone normalization, normalized email syntax, UTC timestamps and soft-archive behavior.
- Tenant-bound application store and EF Core persistence, with DbContext tenant guard and tenant query filter.
- `GET /api/v1/customers` with bounded pagination (default page 1, page size 50; maximum page size 100), `GET /{id}`, `POST`, `PATCH /{id}` and `POST /{id}/archive`.
- Owner, Manager and Receptionist read/write access subject to persisted tenant capabilities/status. Archive is restricted to Owner and Manager. Professional has no general customer-directory access.
- Migration `016_create_tenant_customers.sql`: composite tenant key, RLS enabled and forced, tenant policy, indexes, contact/timestamp constraints, no runtime DELETE/TRUNCATE grant and mandatory customer audit events.
- Soft archive is idempotent; archived customers cannot be updated. Audit is transactional and actor context is mandatory.
- PII is never written into audit payloads. No free-form notes, marketing consent, outreach or customer deletion behavior was invented. Phone numbers are not unique because shared household numbers are a valid case and no deduplication policy was approved.

## CI evidence — run 44587416282

All workflow steps completed successfully on exact head `2a4a1aff0b3a53b34167981fd894092bb0b8f7a3`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: success (8 passed).
- Locked dependency restore and Release build: success, 0 warnings.
- Domain checks: 37/37 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 19/19 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- Customer domain checks: normalization, missing contact, invalid E.164, invalid email, control characters, update semantics, soft archive, idempotency, archived-update rejection and UTC validation.
- Customer persistence checks: tenant A/B isolation, known-UUID lookup isolation, cross-tenant insert blocked by RLS, direct runtime table access denied, creation/archive audit events and rollback/connection-pool safety.
- Customer HTTP checks: receptionist create/list, validation, E.164 normalization, pagination limits, receptionist archive denied; owner create/update/archive; antiforgery enforced.
- HTTP authentication, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable database credentials cleared and PostgreSQL container removed.

## Privacy and product boundaries

This slice stores only customer name and optional phone/email. A contact method is not marketing consent and does not authorize WhatsApp/email campaigns. Contact verification, consent purposes/sources/revocation, retention/legal hold, export and erasure workflows require their own reviewed model before messaging or privacy-request features are exposed. The app normalizes phone input but does not claim the number is verified or deliverable.

## Historical checkpoints preserved

V23–V82 remain unchanged. V79–V81 proxy protections and V82 cross-platform Data Protection certificate support remain passing.

## Next work

Prioritize appointment scheduling as the core operational workflow, starting with reviewed availability/working-hours model and then transactional booking with concurrency-safe overlap prevention, idempotency and audit. Preserve tenant boundaries and time-zone correctness (tenant IANA time zone, UTC instants, explicit handling of ambiguous/nonexistent local times). Do not infer business hours or provider calendars from incomplete seed data.

Continue shared/distributed authentication rate limiting and multi-instance tests. Do not select a production Redis/managed service or deployment topology without an operational path. Keep A1/A2 administrative endpoints blocked pending explicit product-owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit.

## Guardrails

- `main` remains untouched.
- No deployment or production data/configuration changes.
- No SES/email, WhatsApp, billing, trial, or payment provider activation.
- No marketing consent inferred from customer contact details.
- Keep trusted-proxy allowlist deny-all unless exact proxy IPs are supplied through approved runtime configuration.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and independent TOTP replay guard.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval.

---
