# Atendee Beauty - Continuity Status V33 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). main untouched. No deploy, no signup.
- Starting point verified before work: local HEAD = remote HEAD = 01a89a01cd7cacd616b63d63c30c6b766b63f8ed, clean tree, CI run 44218189013 success (docs-only V32 commit).
- Code SHA of this increment: 57eb581ddd425087f5c05955e56e8ea86ce610a8.

## Increment: audit of tenant status changes (Priority A, part 4)
Migration 007 (db/migrations/007_audit_tenant_status_changes.sql, applied in CI after 006):
- New nullable ledger columns: TargetTenantId, FromTenantStatus, ToTenantStatus (no personal data). TargetIdentityUserId is now nullable.
- CHECK CK_SecurityAuditEvents_TargetKind: a tenant_status.changed event must have a tenant target, from/to status in 0..7 and from <> to, and NO identity target; every other event must keep an identity target and NO tenant columns. Existing event kinds are unaffected.
- Trigger tr_tenants_status_audit on atendee.tenants (AFTER UPDATE OF status, only when the status actually changes) calls SECURITY DEFINER function identity.audit_tenant_status_change(), same pattern and ownership as the V30 membership trigger. The audit row shares the transaction of the status change.
- Actor is NULL by design (the database cannot trust a caller-supplied actor; no application path changes tenant status yet).
- tenant_status.changed is intentionally NOT in the C# writer allowlist: only the trigger writes it.
- Status INSERTs (tenant creation) are not audited; only changes.

## Tests (tests/Atendee.Api.HttpChecks) - observed PASS in the CI log
- Suspended (5) -> Closed (7): exactly one event, from 5 to 7, no actor, no identity target.
- Active (3) -> Closing (6): exactly one event with the exact from/to.
- A tenant UPDATE that leaves the status unchanged adds no event.

## CI
- GitHub Actions run 44218855206: completed / success on SHA 57eb581 (PostgreSQL 18.7). Domain 19/19, persistence 9/9, Identity 23/23, HTTP step final RESULT line printed; the 3 new assertions above were seen in the job log.
- Local: Release build of the HTTP checks 0 warnings / 0 errors, git diff --check clean. Integration checks cannot run locally (no PostgreSQL/Docker); CI is the validation path.
- CI evidence only; not production approval.

## Limitations
- The table owner / superuser can still disable the trigger or alter the ledger; not tamper-proof against them.
- The runtime role that has INSERT on the ledger could, in principle, insert a well-formed tenant_status.changed row (the CHECK validates shape, not origin). Same class of limitation as the V30 membership events. Consider a stricter insert path (e.g. separate insert-only role per event family) in a later hardening step.
- Actor is not recorded for trigger-written events; when an administrative status-change flow exists it must pass a trusted actor.
- No audit query/export endpoint exists.
- The tenant-status ACCESS policy is still PROVISIONAL pending owner confirmation (see V32 and CONTINUE_HERE).

## Remaining work
1. Owner confirmation of the tenant-status access policy (still open) and the undecided items: PastDue grace period, post-Closed retention/export, who changes a tenant status.
2. Priority A is now essentially covered (login outcomes, MFA, membership changes, tenant status changes). Remaining audit gap: future administrative actions, to be audited when authorized.
3. Priority C: administrative design document only (disable/revoke, membership management, audit review/export, Platform Admin, break-glass), no endpoints.
4. Other security items: distributed rate limiting and trusted proxy policy, Data Protection key management, WebAuthn, email/signup/invite/reset, restore drill, independent security review, ValidationInterval = 0 performance.
