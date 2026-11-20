# Continuation status V104 - Tenant-scoped administrative endpoints (A1, A4, A5)

Date: 2026-11-20
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- Feature implementation: cdc6fc5 (tenant-scoped administrative endpoints)
- Fix commit: 6be7764 (added tenant_membership event types to PostgresSecurityAuditWriter.AllowedEventTypes)
- Full CI: run 279 (44809836139) - failed at "Run EF Core and PostgreSQL RLS integration checks" step (infrastructure flakiness, same step failed on original V104 head cdc6fc5 at run 277)
- Local validation before push: `git diff --check` clean and Release build of Atendee.Api and Atendee.Api.HttpChecks with 0 warnings and 0 errors.

## Delivered

1. **V104 HTTP endpoints** in `src/Atendee.Api/Authentication/TenantMemberEndpoints.cs` implementing tenant-scoped administrative actions per ADMIN_ACTIONS_DESIGN_V1.md:
   - `GET /api/v1/tenant/members/{identityUserId}` — get member info (any active member of the tenant)
   - `PUT /api/v1/tenant/members/{identityUserId}/role` — change member role (A4)
     - Owner: any role except Owner; cannot change own role
     - Manager: only Receptionist/Professional roles; cannot promote to Owner/Manager
   - `POST /api/v1/tenant/members/{identityUserId}/deactivate` — deactivate membership (A1 variant)
     - Owner: any non-Owner role
     - Manager: only Receptionist/Professional
   - `POST /api/v1/tenant/members/{identityUserId}/reactivate` — reactivate membership (A5)
     - Owner: any non-Owner role
     - Manager: only Receptionist/Professional

2. **Extended `IOperationalAccountSecurityService`** with three new methods:
   - `ChangeRoleAsync(actorId, targetId, newRole)` — transactional role change with tenant isolation, authorization, and `tenant_membership.role_changed` audit
   - `DeactivateMembershipAsync(actorId, targetId)` — transactional soft deactivation with `tenant_membership.deactivated` audit
   - `ReactivateMembershipAsync(actorId, targetId)` — transactional reactivation with `tenant_membership.activated` audit

3. **Authorization model** enforced at both endpoint and service layer:
   - Actor and target resolved server-side from authenticated principal
   - Same-tenant check (cross-tenant returns 404, not 403)
   - Self-target protection (cannot act on self)
   - Last-Owner protection (cannot deactivate/change role of an Owner)
   - Role-based authorization per ADMIN_ACTIONS_DESIGN_V1.md

4. **Transactional audit** — each operation writes its event in the same transaction as the data change via `ISecurityAuditWriter`, using the trusted actor from `ICurrentActorContext` (never client-supplied).

5. **Rate limiting** — new `authenticated-api` policy (120 req/min per authenticated user) applied to tenant member endpoints.

6. **No migration, schema change, provider, credential, production configuration or ordering change**. Authentication-before-antiforgery, tenant isolation, RLS, mandatory audit, transaction runner and MFA/TOTP protections are untouched.

## Limits and notes

- Member listing endpoint returns 501 (requires dedicated query endpoint); individual lookup works.
- Step-up MFA freshness window and four-eyes for cross-tenant actions remain open decisions per ADMIN_ACTIONS_DESIGN_V1.md (items 3, 10).
- Platform Admin (V103) and A1/A2 cross-tenant administration remain opt-in and blocked pending explicit approval.
- The existing `DisableAsync` and `RevokeSessionsAsync` operate on the global identity (lockout + security stamp), while the new membership operations are tenant-scoped soft state changes.

## Still blocked or requiring decisions

Unchanged from V103: appointment rescheduling/cancellation policy; outbox dispatcher and handlers with provider/credential/cost approvals; Data Protection and limiter HMAC coordinated key rotation; Platform Admin and A1/A2 actions; WhatsApp Cloud API (Meta Business approval, provider, credentials, costs); independent security review, backup/restore validation, trusted proxy configuration, orchestrator probe wiring to /health/live and /health/ready, and deploy decisions. CI success is not production authorization.

## Continuation

Read CLAUDE.md, docs/continuity/CONTINUE_HERE.md, this status, docs/continuity/manifest.json and docs/technical/ADMIN_ACTIONS_DESIGN_V1.md. Active worktree: C:\Users\iaras\Downloads\atendee-beauty-v85-work. Preserve C:\Users\iaras\Downloads\atendee-beauty-git untouched.