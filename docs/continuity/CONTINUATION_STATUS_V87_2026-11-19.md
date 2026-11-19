# Continuity status V87 - Authoritative service availability

Date: 2026-11-19
Branch: security/csrf-webhook-antiforgery-v23
Status: APPROVED - FULL CI GREEN

## Commits

- Implementation: 2833e52aeddb063bfb85e16ead1c8f89fbb257ab (feat: calculate tenant-aware service availability)
- Test correction: 8e0b70e66bf59833a7474fd6f9a8ef7b0476afee (test: include exact-fit buffered availability window)
- Validated head: 8e0b70e66bf59833a7474fd6f9a8ef7b0476afee
- Full CI: GitHub Actions run 44638633076 (run 234), success.

## Implemented

- Added ServiceAvailabilityCalculator in the Domain scheduling layer.
- Added authenticated GET /api/v1/availability with required inclusive local-date range (maximum 31 days), required active service and optional professional filter.
- Availability is the intersection of active tenant business hours (V84), active professional recurring weekly availability (V85), date-specific full-day and partial-day time-off exclusions (V86), and active professional/service assignment.
- Service duration and before/after buffers are included in earliest/latest valid start boundaries.
- Results use UTC instants and one-minute start precision. Earliest and latest start boundaries are inclusive.
- DST behavior uses UTC timeline traversal: nonexistent local wall minutes do not exist in the timeline; both occurrences of ambiguous wall time map to distinct UTC instants. No arbitrary offset is selected.
- Team read: Owner, Manager and Receptionist. Professional read: own profile only. No client-supplied tenant context.
- Database reads are completed inside the trusted tenant transaction; CPU-bound calculation runs after the transaction commits.
- Range validation, unknown/inactive service handling, professional IDOR protection and date-boundary checks are covered.

## API

GET /api/v1/availability?from=YYYY-MM-DD&to=YYYY-MM-DD&serviceId={id}&professionalId={optional}

Response contains tenant time-zone ID, requested local date range, service ID and per-professional windows:
- earliestStartUtc
- latestStartUtc

A returned window represents minute-aligned service start instants within its inclusive boundaries, not an appointment reservation.

## CI evidence - run 44638633076

All workflow steps completed successfully on exact head 8e0b70e66bf59833a7474fd6f9a8ef7b0476afee:

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed.
- Locked dependency restore and Release build: success, zero warnings and zero errors.
- Domain checks: 69/69 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 32/32 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- HTTP availability integration tests passed, including tenant schedule intersection, service assignment, time-off subtraction, exact-fit buffered start, range rejection, inactive/unknown service and Professional own-profile-only access.
- Existing HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: passed.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable credentials cleared and PostgreSQL container removed.

Run 233 (44638161303) failed only because the first HTTP assertion omitted a valid exact-fit window. The calculation was correct: a 60-minute open interval fits a 45-minute service with 5-minute before and 10-minute after buffers at exactly one start minute. The test was corrected; run 234 passed all steps.

## Explicit boundaries

- V87 computes schedule-based availability but does not subtract existing appointments because appointment persistence does not yet exist.
- No booking is confirmed by this endpoint. A window is not a reservation.
- No migration was needed for V87.
- No deployment, production change, email/WhatsApp integration, billing/payment activation or main-branch change occurred.

## Next implementation

V88: transactional appointment creation and reads:
1. Tenant-scoped appointment table with same-tenant FKs and RLS.
2. Immutable service, price, duration, buffer, customer and professional snapshots.
3. Required idempotency key and canonical request hash; replay returns the original appointment, key reuse with a different request returns conflict.
4. Exact UTC minute-aligned start validated against current authoritative availability inside the write transaction.
5. PostgreSQL per-tenant/per-professional advisory lock plus deferred constraint trigger rejects overlapping occupancy intervals, including direct SQL and concurrent requests.
6. Append-only appointment events, mandatory audit and transactional outbox in the same commit.
7. Concurrency tests prove exactly one winner for incompatible overlapping reservations.

Then implement state transitions, check-in, in-service, completion, no-show, cancellation and atomic rescheduling. Active appointments must be subtracted from availability. Cancellation policy remains a product configuration decision; do not invent customer-facing cancellation rules.

## Guardrails

- main remains untouched; no deployment or production configuration/data changes.
- Do not configure SES/email, WhatsApp, AI, billing, trial or payment providers without required product/credential/cost decisions.
- Platform Admin and A1/A2 administrative endpoints remain blocked pending explicit approval of identity realm, scope, step-up MFA/four-eyes, last-Owner and audit rules.
- Preserve existing authentication, antiforgery, tenant isolation, RLS, transaction runner, audit and TOTP replay protections.
- Use the clean worktree C:\Users\iaras\Downloads\atendee-beauty-v85-work; preserve the older invalid clone untouched.

---