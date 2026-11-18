# Atendee Beauty - Continuity Status V32 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). main untouched. No deploy, no signup.
- Verified at the start of this session: local HEAD = remote HEAD = fa8d6067ab8e7f09b18a16f0363a1372d6df344f, clean working tree.
- Code SHA of this increment: fa8d6067ab8e7f09b18a16f0363a1372d6df344f.

## Increment: deny-by-default tenant status access policy + authorization matrix (commit fa8d606)
This commit was made in a previous session and had NO status file until now (this file closes that gap).
- TenantAccessPolicy (status x role x capability) in src/Atendee.Domain/Tenants/, applied at login, MFA challenge, MFA enrollment and on every authenticated request (TenantContextMiddleware).
- Product decision record: docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md.
- Endpoint authorization matrix: docs/technical/AUTHORIZATION_MATRIX_V1.md (covers Priority B for the endpoints that exist today; no business endpoints exist yet).

## OPEN POINT - needs the product owner's explicit confirmation
The original handoff (section 7) says the tenant-status access matrix must NOT be decided by the AI without explicit user confirmation. The commit and the policy document state the decision was taken under the user's delegation ("Decida tudo da forma mais profissional"). A later session could NOT verify that delegation from the repository alone.
- Treat the policy as PROVISIONAL until the owner confirms it (or changes it).
- It is reversible: change a row in TenantAccessPolicy and the exhaustive domain check. No real data, deploy or signup depends on it.
- Do not build further enforcement on top of it (business endpoints, status-change flows) before the owner confirms.
- Still undecided by anyone: PastDue grace duration and PastDue -> Suspended process, post-Closed retention/export/deletion, who/how changes a tenant status.

## CI (observed, not inferred)
- GitHub Actions run 44217401209: completed / success on SHA fa8d606 (PostgreSQL 18.7).
- Domain checks 19/19, persistence 9/9, Identity 23/23; HTTP step final line reported authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks passed.
- CI evidence only; not production approval.

## Limitations
- Same as V31: privileged DB owner/superuser can alter audit data; TOTP challenge failure event is written outside the failed-attempt transaction; no audit query/export endpoint.
- Tenant status changes are not audited yet (only membership changes are, via DB trigger).
- Integration checks cannot run locally on the remote desktop (no PostgreSQL/Docker); CI is the validation path.

## Remaining work
1. Owner confirmation of the tenant-status policy (see open point).
2. Audit of tenant status changes (DB trigger, same pattern as the V30 membership trigger) - safe and independent of the policy decision.
3. Priority C: administrative design only (disable/revoke, membership management, audit review/export, Platform Admin), no endpoints.
4. Other security items from the handoff: distributed rate limiting and trusted proxy policy, Data Protection key management, WebAuthn, email/signup/invite/reset, restore drill, independent security review, performance of SecurityStampValidatorOptions.ValidationInterval = 0.
