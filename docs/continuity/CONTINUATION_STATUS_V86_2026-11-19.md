# Atendee Beauty - Continuity Status V86 (approved, 2026-11-19)

## Result

V86 adds tenant-scoped, date-specific professional time off with full-day and partial-day entries. The feature is persisted and audited, but is intentionally not yet wired into an authoritative availability/slot calculation.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44633827726)
- Run: `44633827726` (run 231), conclusion `success`.
- Exact fully validated head: `0be194941318e38ee7cbaa2e0fc465eba287c8bd`.
- Feature implementation commit: `3f6d64b4b6a725ef502767476e50c69c7fd56a2f`.
- Follow-up test permission correction: `0be1949`.
- Branch: `security/csrf-webhook-antiforgery-v23`.
- No deployment, production data/configuration change or external provider activation.

## Scope V86 - date-specific time off

Added:

- `GET /api/v1/professionals/{professionalId}/time-off?from=YYYY-MM-DD&to=YYYY-MM-DD`.
- `POST /api/v1/professionals/{professionalId}/time-off`.
- `POST /api/v1/professionals/{professionalId}/time-off/{timeOffId}/archive`.
- The inclusive GET range is required and bounded to at most 366 calendar days.
- Date and optional times are interpreted in the tenant's configured time zone. Responses include `timeZoneId`; values remain local calendar dates and wall-clock `HH:mm`, not UTC instants.
- Full-day time off is represented by both `startTime` and `endTime` being null. Partial-day entries require both times and a strictly increasing interval on the same local date.
- Up to four active entries per date. Adjacent partial-day intervals are allowed; overlaps are rejected. A full-day entry is exclusive for its date.
- Time off is subtractive only: it blocks time from the recurring professional schedule and does not add availability outside that schedule.
- Owner/Manager can create and archive team time off. Owner/Manager/Receptionist can read team entries. A Professional can read only their own entries and cannot create/archive them.
- Migration `019_create_professional_time_off.sql`: composite tenant key, FK to an active same-tenant professional, RLS enabled and forced, tenant-scoped indexes, full-day and exact-interval uniqueness, no runtime DELETE/TRUNCATE grant and mandatory audit.
- A per-tenant/per-professional transaction advisory lock serializes application and direct SQL writes. A deferred PostgreSQL constraint trigger independently enforces full-day exclusivity, no overlaps and the four-entry daily cap.
- Creation and archive events are written to the mandatory tenant audit ledger in the same transaction. Entries are soft-archived; runtime cannot physically delete them.
- EF Core mapping, tenant ownership validation, scoped store registration and API registration are included.

## CI evidence - run 44633827726

All workflow steps completed successfully on exact head `0be194941318e38ee7cbaa2e0fc465eba287c8bd`:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore and Release build: success.
- Domain checks: 63/63 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 32/32 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- HTTP checks passed, including create/list/archive, invalid range, overlap rejection, full-day exclusivity, tenant time-zone preservation, audit, RLS isolation, no tenant-context reads, no runtime DELETE/TRUNCATE, Professional own-read and Professional write denial.
- Direct SQL attempting to add partial time off over a full-day entry was rejected by the deferred database constraint trigger.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable credentials cleared and PostgreSQL container removed.

## Boundaries and remaining work

Time off is stored and secured but is not yet subtracted from calculated slots because authoritative slot calculation is not implemented. Do not expose appointment booking until all availability sources are combined and DST behavior is explicit.

This increment does not introduce date-specific extra working-hour overrides, recurring leave ranges, time-off approval workflows, employee requests or notifications. Full-day and partial-day entries are manager-controlled blocks. These are deliberate scope boundaries, not implied capabilities.

## Next work

1. Implement authoritative availability calculation combining:
   - tenant business hours (V84),
   - recurring professional weekly availability (V85),
   - active date-specific time-off exclusions (V86),
   - active professional/service assignment,
   - service duration and before/after buffers.
2. Use the tenant's configured time zone and define explicit behavior for ambiguous and nonexistent local times at DST transitions. Keep local schedule values distinct from UTC appointment instants.
3. Add deterministic tests for split shifts, full-day/partial-day time off, date boundaries, DST gaps/overlaps and service buffers.
4. Only then implement transactional appointment booking: PostgreSQL overlap prevention, idempotency keys, immutable service/price/duration snapshots, legal state transitions, cancellation/rescheduling rules and append-only audit.
5. In parallel, close shared/distributed authentication rate limiting with multi-instance concurrency tests without assuming an unverified production Redis/managed-service topology.
6. Keep Data Protection certificate rotation blocked until old-key decryption support and a coordinated rotation plan exist.

## Workspace and guardrails

- Active clean worktree used for V86: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`.
- The older `C:\Users\iaras\Downloads\atendee-beauty-git` directory had an invalid/missing Git HEAD and showed all files as untracked. It was not modified or cleaned; it remains preserved for manual recovery.
- `main` remains untouched; no deployment, production data/configuration change, SES/email, WhatsApp, billing, trial or payment provider activation.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.
- Keep A1/A2 administrative endpoints blocked pending explicit product-owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit.

---
