# Administrative actions - design V1 (PROPOSAL, nothing implemented)

Status: design only. No endpoint, service or migration described here exists. Every item marked **DECISION** needs the product owner before implementation. This document does not claim production readiness.

Branch context: `security/csrf-webhook-antiforgery-v23`. Builds on `docs/technical/AUTHORIZATION_MATRIX_V1.md` and the audit ledger (`identity."SecurityAuditEvents"`, migrations 003-007).

## 1. Principles

1. Deny by default; an action with no row in section 4 does not exist.
2. Actor, tenant, role and target are always resolved server-side from the authenticated principal and persisted membership. Nothing is taken from the client.
3. No action without: authorization check, step-up authentication where listed, antiforgery, rate limit, transactional audit event.
4. Mandatory audit is fail-closed: if the event cannot be written in the same transaction, the action fails.
5. Least privilege: a tenant Owner is NOT a Platform Admin. Cross-tenant power lives in a separate identity realm.
6. High-impact actions are reversible where possible, time-delayed or two-person where not.
7. Reasons are enumerated codes, not free text, to avoid storing personal data in the ledger.

## 2. Actors

| Actor | Scope | Authentication |
|---|---|---|
| Owner (tenant) | own tenant only | password + TOTP (current) |
| Manager (tenant) | own tenant only, never above Owner | password + TOTP (current) |
| Receptionist / Professional | none of the actions below | password only (current) |
| Platform Admin | cross-tenant, explicit and logged | **DECISION**: separate identity store and login surface; phishing-resistant MFA (WebAuthn) mandatory; no shared accounts |
| System (billing / lifecycle jobs) | specific transitions only | service identity, never a human session |

Platform Admin must not be representable as a tenant membership role. Proposal: separate table/realm with its own cookie scheme, its own rate limiter and no access to tenant business data by default (see break-glass, section 6).

## 3. Cross-cutting controls

- **Step-up:** sensitive actions require an MFA proof newer than N minutes (**DECISION**, proposal 10 min) in addition to the session `amr=mfa`. Stale proof returns 403 with a re-challenge hint, never a silent pass.
- **Trusted actor propagation to the database:** today trigger-written events store a NULL actor. Proposal: the server sets a transaction-local setting (`SET LOCAL app.actor_id`) from the authenticated principal inside the same transaction; triggers read it with `current_setting(..., true)`. Only the application role can set it, and its absence must remain valid (NULL actor) for maintenance paths. Needs an adversarial test that a client value can never reach it.
- **Last-owner protection:** a tenant must always keep at least one active, MFA-enrolled Owner. Enforce in the database (constraint trigger), not only in code.
- **No self-escalation:** nobody changes their own role or reactivates themselves. A Manager cannot grant or remove Owner and cannot act on an Owner.
- **Idempotency and concurrency:** each mutating action carries an idempotency key and uses optimistic concurrency on the target row.
- **Rate limits:** dedicated limiter per action family, keyed by authenticated actor (not by client-supplied value). Behind a trusted-proxy policy (still open, see handoff).
- **Notification:** role change, disable, revoke and status change notify the affected user and tenant Owners through a verified channel. Depends on the email provider (not built).
- **Antiforgery:** unchanged; no bypass for any admin route.

## 4. Action catalog (proposal)

| # | Action | Who | Preconditions | Step-up | Audit event (new unless noted) |
|---|---|---|---|---|---|
| A1 | Revoke a member's sessions | Owner (any member of own tenant); Manager (Receptionist/Professional only) | target in same tenant, active membership | yes | `operational_account.sessions_revoked` (exists; add actor) |
| A2 | Disable a member's account | Owner (own tenant); Platform Admin | not self; not last Owner; target in scope | yes (+ four-eyes if Platform Admin cross-tenant) | `operational_account.disabled` (exists; add actor) |
| A3 | Invite member | Owner; Manager (non-Owner roles) | email provider, signed single-use invite, expiry | yes | `tenant_membership.invited`, `tenant_membership.created` (exists) |
| A4 | Change member role | Owner; Manager only among Receptionist/Professional | not self; not last Owner; promotion to Owner needs MFA enrollment before it takes effect | yes | `tenant_membership.role_changed` (exists; add actor) |
| A5 | Deactivate / reactivate membership | Owner; Manager (non-Owner) | not self; not last Owner | yes | `tenant_membership.deactivated` / `activated` (exist; add actor) |
| A6 | Change tenant status | System (billing/lifecycle) and Platform Admin only | allowed transition per graph below; reason code | yes + four-eyes for `Closed` | `tenant_status.changed` (exists; add actor and reason code) |
| A7 | View tenant audit trail | Owner (own tenant) | read-only, paginated, filtered by tenant | yes | `audit.viewed` |
| A8 | Export tenant audit trail | Owner (own tenant) | rate-limited, size-capped, no email/IP columns | yes | `audit.exported` |
| A9 | Cross-tenant audit review | Platform Admin | justification code, scoped to one tenant at a time | yes | `platform_admin.audit_viewed` |
| A10 | Break-glass data access | Platform Admin | section 6 | yes | `platform_admin.break_glass_*` |

Tenant status transition graph (**DECISION**; proposal, consistent with the provisional access policy):
`PendingVerification -> SetupRequired -> Trial/Active`; `Trial/Active <-> PastDue`; `PastDue -> Active | Suspended`; `Suspended -> Active | Closing`; `Closing -> Closed | Suspended`; `Closed` is terminal for self-service. Any transition outside the graph is rejected by a database constraint/trigger, not only by application code. Who may perform each edge, the PastDue grace duration and post-Closed retention remain open.

## 5. Audit requirements

- Every action in section 4 writes its event in the same transaction as the change, with a trusted actor (never client supplied) and target. Reason codes are an allowlist.
- **Gap found:** events with an identity target carry no tenant, so a tenant-scoped audit view (A7/A8) cannot be filtered safely today. Proposal: add `TargetTenantId` to identity-target events, resolved transactionally from the membership (never from the request), then scope reads by it.
- **Read path:** a dedicated read-only database role limited to a view that filters by tenant; the application runtime role keeps INSERT only. A7/A8 never expose email, IP, TOTP or recovery material (none is stored).
- **Integrity:** the ledger resists tampering by the application role only. Owner/superuser can still alter it. Mitigations to design later: periodic hash-chain or external append-only copy; do not describe the ledger as immutable.
- **Retention and deletion:** product decision, open.

## 6. Break-glass (Platform Admin access to tenant data)

Default is NO access to tenant business data. When a legitimate support or legal need exists:
1. Request with an enumerated reason code and ticket reference; a second Platform Admin approves (four-eyes).
2. Time-boxed grant (proposal: 60 min), scoped to one tenant and read-only unless explicitly approved otherwise; auto-expires.
3. Separate short-lived credential and fresh WebAuthn proof; no reuse of a normal session.
4. Events: requested, approved, activated, expired, plus every read; tenant Owners are notified.
5. Mandatory post-incident review record.

## 7. Threats and mitigations

| Threat | Mitigation |
|---|---|
| Privilege escalation via role change | no self-change; Manager cannot touch Owner; promotion to Owner requires prior MFA enrollment; database last-owner constraint |
| IDOR across tenants | tenant resolved from membership; row-level security plus an explicit application check; cross-tenant negative tests |
| CSRF or replay of admin calls | antiforgery on every unsafe method; idempotency keys; step-up freshness |
| Stolen session performs admin action | step-up MFA; short freshness window; security-stamp revalidation each request |
| Audit evasion or forged actor | transactional fail-closed events; actor from server principal only; adversarial test for client-supplied actor |
| Insider Platform Admin abuse | separate realm, WebAuthn, four-eyes, break-glass time-box, notifications, review |
| Self-lockout / tenant takeover | last-Owner constraint; recovery path through Platform Admin with four-eyes |
| Audit flood (DoS) | per-action limiters; essential events never sampled away |

## 8. Required tests per action (before any endpoint ships)

Allowed role; each denied role; each denied tenant status (per `TenantAccessPolicy`); stale step-up; no MFA; cross-tenant target; self-target; last-Owner target; concurrent request; replayed idempotency key; mid-session membership or status change; unauthenticated; and an assertion that exactly one audit event with the correct trusted actor and target is persisted in the same transaction (and none when the action is rejected).

## 9. Suggested implementation order (each step small, CI-verified)

1. Trusted actor propagation to the database plus adversarial test (no new endpoint).
2. `TargetTenantId` on identity-target events and a tenant-scoped read view with a dedicated read role (no endpoint).
3. Last-Owner and transition-graph database constraints with tests.
4. Step-up authentication primitive with tests.
5. Tenant-scoped actions A1, A4, A5 (internal service first, then endpoint) with the full test matrix.
6. A7/A8 audit view/export.
7. Platform Admin realm, A2 cross-tenant, A6, A9, A10 (largest; needs independent security review before exposure).

## 10. Decisions needed from the product owner

1. Confirm or change the provisional tenant-status access policy.
2. Platform Admin model: separate realm and WebAuthn requirement; who the first administrators are.
3. Step-up freshness window and which actions require four-eyes.
4. Tenant status transition graph, who performs each edge, PastDue grace period.
5. Audit retention, tenant-visible audit scope, and post-Closed retention/export/deletion.
6. Break-glass duration and approval rules.

## 11. Non-goals of this document

No deploy, no public signup, no provider integration, no endpoint, no change to `main`, and no weakening of any existing guardrail.
