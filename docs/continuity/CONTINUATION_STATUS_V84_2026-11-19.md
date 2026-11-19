# Atendee Beauty — Continuity Status V84 (approved, 2026-11-19)

## Result

V84 adds tenant-scoped weekly business-hours configuration and database-enforced interval integrity. It also hardens the shared tenant transaction runner so a deferred PostgreSQL constraint failure remains the original exception when Npgsql has already completed the transaction and a rollback attempt is no longer valid.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44590386383)
- Run: `44590386383`, conclusion `success`.
- Business-hours implementation commit: `6417a46953e3504a597784edb7215bfeb157a732`.
- Follow-up persistence namespace correction: `99c8b3a`.
- Transaction-runner correction and exact validated head: `5716f69d6f9d1f8032da150797d3572bad876909`.
- No production deployment, production data change or provider activation.

## Scope V84 — weekly business hours

Added:

- `GET /api/v1/business-hours` and `PUT /api/v1/business-hours`.
- Schedule uses the tenant's persisted time-zone ID; interval values are local wall-clock `HH:mm`, not UTC instants.
- ISO weekdays (Monday=1 through Sunday=7), up to four intervals per day and 28 per week.
- Empty schedule means closed all week. No default business hours are inferred.
- Multiple same-day intervals support split shifts. Adjacent intervals are allowed; overlapping intervals, zero-length intervals, overnight intervals, invalid days and invalid time strings are rejected.
- Owner and Manager can replace the weekly schedule; Receptionist can read; Professional cannot access the salon-wide business-hours configuration.
- Migration `017_create_tenant_business_hours.sql`: tenant composite key, RLS enabled and forced, owner policy for safe security-definer trigger execution, active interval index, no runtime DELETE/TRUNCATE grant and mandatory transactional audit events.
- Full schedule replacement uses a tenant-scoped transaction advisory lock. A deferred database constraint trigger independently enforces no more than four active intervals per day and rejects overlaps, including direct SQL writes and concurrent transactions.
- Removed intervals are soft-archived; unchanged interval identities are retained; repeating the same schedule is idempotent.
- Transaction runner preserves the original deferred-constraint exception if Npgsql marks a transaction complete and rejects a subsequent rollback call.

## CI evidence — run 44590386383

All workflow steps completed successfully on exact head `5716f69d6f9d1f8032da150797d3572bad876909`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore and Release build: success, 0 warnings and 0 errors.
- Domain checks: 46/46 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 30/30 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- Business-hours domain checks: weekday ordering, empty schedule, invalid weekdays/times, overnight/overlap rejection, adjacency, four-interval daily cap and 28-interval weekly cap.
- Business-hours persistence checks: tenant A/B isolation, RLS cross-tenant insert rejection, direct runtime read denied, physical delete denied, audit events, overlap rejection at database commit, soft archive, unchanged identity retention and idempotent replacement.
- HTTP checks: Receptionist read allowed/write denied, Owner schedule replacement/readback, invalid/overlapping input rejected without overwriting valid configuration, Professional access denied.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable credentials cleared and PostgreSQL container removed.

## Boundaries and remaining work

Business hours are configuration only; this increment does not calculate available slots, store professional availability, create appointments or reserve inventory. The weekly times are local wall-clock values. Date-specific conversion, daylight-saving gaps/ambiguities and appointment instants must be resolved explicitly in the availability/booking layer before any booking endpoint is exposed.

No business hours are inferred from locale or seed data. Empty schedule is closed. No WhatsApp/email, payments, billing or production configuration is activated.

## Historical checkpoints preserved

V23–V83 remain unchanged. V79–V81 proxy protections, V82 certificate-backed cross-platform Data Protection, and V83 tenant customer management remain passing.

## Next work

1. Add professional weekly availability and date-specific exceptions/time off, intersected with salon business hours and assigned services.
2. Implement timezone-safe availability calculation using the tenant's configured time zone and explicit DST ambiguous/nonexistent local-time policy.
3. Implement appointment booking only after availability is authoritative: atomic overlap prevention, idempotency keys, service/price/duration snapshots, legal state transitions, cancellation/rescheduling rules and transactional audit.
4. Close shared/distributed authentication rate limiting with multi-instance concurrency tests without assuming an unverified production Redis/managed-service topology.
5. Add consent/retention/privacy-request flows before marketing outreach or customer erasure.

Keep A1/A2 administrative endpoints blocked pending explicit product-owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit.

## Guardrails

- `main` remains untouched.
- No deployment or production data/configuration changes.
- No SES/email, WhatsApp, billing, trial or payment provider activation.
- Customer contact data is not marketing consent.
- Trusted proxy allowlist remains deny-all unless exact proxy IPs are supplied through approved runtime configuration.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and independent TOTP replay guard.

---
