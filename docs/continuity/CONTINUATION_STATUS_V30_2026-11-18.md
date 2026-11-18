# Atendee Beauty - Continuity Status V30 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). main untouched. No deploy, no signup.
- Previous code SHA with green CI: 418699ddbcf22d198519570a7cc5412a678934cb (run 44213223276). Docs-only 8e5985e followed (CI run 44213787047 was in progress when V30 work began).

## Increment: database-level audit of membership changes (Priority A, part 2)
Finding: no application code path creates or changes tenant memberships yet (only test SQL does). Auditing in app code would therefore be dead code, so the audit is enforced in the database.

Migration 005 (db/migrations/005_audit_tenant_membership_changes.sql, applied in CI after 004):
- Extends the audit CHECK constraint with tenant_membership.created, .role_changed, .activated, .deactivated.
- Trigger tr_tenant_users_audit on atendee.tenant_users (AFTER INSERT OR UPDATE OF role, is_active, FOR EACH ROW) calls identity.audit_tenant_membership_change(), a SECURITY DEFINER function owned by atendee_identity_owner (search_path pinned, EXECUTE revoked from PUBLIC and granted only to atendee_owner, the table owner that creates the trigger).
- The audit row is written in the same transaction as the membership change, whatever code path performs it. A simultaneous role + active change yields two events.
- Actor is NULL by design: the database cannot trust a caller-supplied actor. When an authorized admin path exists, the actor must come from a trusted server-side source (decision pending with Priority C).
- No tenant ID, role value, display name or PII is stored in the event.

Limitations:
- Privileged DB owner/superuser can still disable the trigger or alter data; not tamper-proof against them.
- Changing identity_user_id of a membership is not audited (not an expected operation).
- Membership deletion is not audited (FK RESTRICT and no delete path exist).

## Tests (tests/Atendee.Api.HttpChecks)
- Receptionist ledger now expects 6 events (adds exactly one tenant_membership.created from seeding).
- Owner->Receptionist role change yields exactly one role_changed and no activation/deactivation events.
- Membership deactivation yields exactly one deactivated and no activated event.
- Release build 0 warnings / 0 errors locally. CI result: see below once observed.

## CI
- Pending for this increment.

## Remaining work
- Audit MFA enrollment failures and password-verified-pending-MFA.
- Priority B endpoint authorization matrix.
- Priority C admin design (disable/revoke, membership management, audit review/export, Platform Admin) - not before A and B.
- Tenant-status access policy still requires the product owner's decision.