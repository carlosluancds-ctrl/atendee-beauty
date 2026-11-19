# Atendee Beauty - Continuity Status V85 (approved, 2026-11-19)

## Result

V85 adds tenant-scoped recurring weekly availability for professional profiles. The schedule is expressed in the salon's persisted time zone using local wall-clock values; it is not converted into UTC instants and does not yet calculate bookable slots.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44595166252)
- Run: `44595166252` (run 229), conclusion `success`.
- Exact validated head: `b68f6feb5869d77f829061f28994029a5ab8b707`.
- Implementation commit: `21d594b` (weekly professional availability).
- Follow-up test isolation commit: `b68f6fe`.
- No production deployment, production data change or provider activation.

## Scope V85 - recurring professional availability

Added:

- `GET /api/v1/professionals/{professionalId}/availability`.
- `GET /api/v1/professionals/me/availability`.
- `PUT /api/v1/professionals/{professionalId}/availability`.
- Schedule values use the tenant's persisted time-zone ID and local `HH:mm` wall-clock times.
- ISO weekdays (Monday=1 through Sunday=7), up to four intervals per day and 28 per week.
- Empty schedule means no recurring professional availability; no default work hours are inferred.
- Split shifts and adjacent intervals are supported. Invalid weekdays, invalid/zero-length/overnight intervals, overlaps and interval-count excess are rejected.
- Owner/Manager can configure; Owner/Manager/Receptionist can read team schedules; a Professional can read only their own schedule and cannot modify it.
- Migration `018_create_professional_availability.sql`: tenant composite key and professional FK, RLS enabled and forced, tenant-scoped indexes, no runtime DELETE/TRUNCATE grant, mandatory transactional audit and soft archival.
- Replacement is serialized with a per-tenant/per-professional transaction advisory lock. A deferred database constraint trigger independently enforces no overlaps and the daily interval cap, including direct SQL writes.
- Unchanged intervals retain identity; removed intervals are soft-archived; repeating the same schedule is idempotent.

## CI evidence - run 44595166252

All workflow steps completed successfully on exact head `b68f6feb5869d77f829061f28994029a5ab8b707`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore and Release build: success.
- Domain checks: 55/55 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 32/32 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable credentials cleared and PostgreSQL container removed.

## Boundaries and remaining work

V85 stores recurring professional availability only. It does not yet combine it with salon business hours, date-specific time off, assigned-service duration/buffers or daylight-saving transitions. No appointment booking or slot reservation is exposed.

## Historical checkpoints preserved

V23-V84 remain unchanged. V84 business-hours configuration and all previous security, tenant isolation, customer, Identity/TOTP, proxy and Data Protection controls remain in force.

## Next work

V86 adds date-specific time off. After that, implement authoritative availability calculation by combining salon business hours, professional weekly availability, date-specific time-off exclusions, assigned services and duration/buffers, with explicit DST ambiguous/nonexistent local-time handling. Only then proceed to transactional appointment booking with overlap prevention, idempotency, state transitions and append-only audit.

Keep shared/distributed authentication rate limiting and Data Protection certificate rotation on the backlog. Do not deploy, configure external providers, activate billing/trial or modify `main`.

---
