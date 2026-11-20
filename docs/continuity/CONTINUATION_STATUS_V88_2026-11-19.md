# Continuation status V88 - tenant appointment booking and lifecycle

Date: 2026-11-19
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- Appointment implementation commit: cdc164100dc80f84706e57c95869d1dd8266223d.
- Corrective HTTP audit-test commit: e7efdc40bc2d6c2e88dbb2e182add2379eeea370.
- Full CI run 237: 44646121213, success on exact head e7efdc40bc2d6c2e88dbb2e182add2379eeea370.
- V87 remains validated on run 234 (44638633076) and its implementation/checkpoint commits are preserved.

## Delivered

1. Migration 020 creates tenant-scoped appointments, append-only appointment events and transactional outbox.
2. RLS is enabled and forced for appointment/event/outbox tables; appointment rows use same-tenant customer, professional, service and creator membership foreign keys.
3. Booking snapshots preserve customer, professional, service, price, currency, duration and before/after buffers.
4. POST /api/v1/appointments requires a validated idempotency key and canonical SHA-256 request hash. Identical retries replay the original appointment; key reuse with a different request conflicts.
5. Booking validates active same-tenant customer/service/professional assignment and rechecks authoritative service availability inside the transaction.
6. Per-tenant/professional advisory locking and a deferred PostgreSQL constraint trigger reject overlapping occupancy, including direct SQL writers and concurrent requests.
7. Appointment lifecycle transitions are domain-validated and database-enforced. Check-in, in-service, completion, cancellation and no-show are represented by legal transitions; invalid jumps conflict.
8. Create and status changes append immutable event history, mandatory tenant audit and outbox messages in the same transaction.
9. Runtime cannot physically delete/truncate appointment history or directly write the outbox.
10. GET /api/v1/appointments and GET /api/v1/appointments/{id} provide bounded tenant-scoped reads. Professionals can read only their own appointments and phone snapshots are redacted.
11. Availability now subtracts blocking appointment occupancy, including buffers; completed, cancelled and no-show appointments no longer block slots.

## CI evidence - run 44646121213 (run 237)

All workflow steps succeeded on exact head e7efdc40bc2d6c2e88dbb2e182add2379eeea370:

- Frontend dependency audit, tests/build and portal browser E2E: 8 passed.
- Locked restore and Release build: succeeded, zero warnings and zero errors.
- Domain checks: 79/79 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 43/43 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookie, session revocation, logout, MFA, audit, role matrix and tenant status checks: passed.
- Appointment HTTP assertions covered snapshots, create/read/list, idempotent replay, mismatched-key conflict, availability subtraction, direct-SQL overlap rejection, legal/illegal lifecycle transitions, audit/event/outbox atomicity and completed-appointment release.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed.
- Disposable database credentials were cleared and PostgreSQL container removed.

Run 236 (44645387873) failed because the audit test queried an owner-only audit ledger using the migrator role. The test was corrected to execute under atendee_owner in an explicit transaction. Run 237 passed all workflow steps.

## Remaining backend work - not claimed complete

1. Distributed authentication rate limiting is still process-local. Implement a shared, atomic backend limiter and multi-instance concurrency tests without assuming Redis or a particular production topology.
2. Data Protection key-ring persistence/protection and certificate rotation require a coordinated key-compatibility/rotation plan and deployment-specific path/secret management. Do not rotate keys or alter production configuration here.
3. Appointment rescheduling semantics and customer cancellation windows are product-policy decisions. The current lifecycle supports legal cancellation/no-show transitions but does not expose a dedicated reschedule workflow or invent customer-facing cancellation rules.
4. The transactional outbox is written atomically but has no dispatcher/consumer yet. Do not mark messages processed or discard them without a real handler and retry/dead-letter semantics. External email, WhatsApp, AI, billing/payment and webhook integrations remain blocked on provider, credential, cost and product decisions.
5. Platform Admin and cross-tenant A1/A2 actions remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner rules and tenant-scoped audit.
6. Production readiness still requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific Data Protection/secret controls.

## Safeguards

- main untouched; no deployment or production data/configuration changes.
- No signup enablement, email/WhatsApp provider configuration, billing/trial/payment activation or external credential changes.
- Existing authentication-before-antiforgery ordering, tenant isolation, RLS, transaction runner, mandatory audit and TOTP replay protections remain intact.
- Keep the older invalid clone C:\Users\iaras\Downloads\atendee-beauty-git untouched. Active worktree remains C:\Users\iaras\Downloads\atendee-beauty-v85-work.