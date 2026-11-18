# Atendee Beauty - Continuity Status V35 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). main untouched. No deploy, no signup.
- Starting point verified: local HEAD = remote HEAD = 6964ba97682c89eb11adc77f764b4f07f7d6c61a, clean tree, CI run 44219922880 success (docs-only). Last code SHA with green CI: 57eb581 (run 44218855206).
- This increment is documentation only (no code, migration or workflow change).

## Owner decision recorded
- 2026-11-18 (~02:02 UTC): the product owner explicitly approved the tenant-status access matrix (status x role x capability) in chat: "Eu aprovo aquela tabela lá", in answer to a specific question about that table.
- The matrix in docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md is therefore APPROVED, no longer provisional. Implementation (TenantAccessPolicy) already matches it and was green in CI (run 44217401209 and later).
- The approval covers ONLY that matrix. Any future change to the table needs a new explicit owner decision.
- Docs updated: POLITICA_ACESSO_STATUS_TENANT_V1.md (approval block), ADMIN_ACTIONS_DESIGN_V1.md (decision 1 resolved, "approved" wording), CONTINUE_HERE.md.

## Still open (not decided by the owner)
- PastDue grace duration and the PastDue -> Suspended process.
- Post-Closed retention, deletion and export; refund/credit policy on cancellation.
- Who and how changes a tenant status (administrative flow; Platform Admin model).
- Platform Admin model (separate realm, WebAuthn), step-up window, four-eyes actions, status transition graph, audit retention and tenant-visible scope, break-glass rules (ADMIN_ACTIONS_DESIGN_V1.md section 10, items 2-6).

## Verification
- git diff --check clean; no code changed, so no new build/test claim is made. CI for the docs commit is recorded in CONTINUE_HERE when observed.

## Remaining work
1. Implementation step 1 of the design (safe without further decisions): trusted actor propagation to the database (transaction-local setting read by the triggers) with an adversarial test that a client-supplied value never reaches it. No endpoints.
2. Step 2: TargetTenantId on identity-target events, tenant-scoped read view and read-only database role.
3. Owner decisions listed above before any status-change flow, Platform Admin or business-endpoint enforcement beyond the approved matrix.
4. Other security items (distributed rate limiting, trusted proxy, Data Protection key management, WebAuthn, email/signup/invite/reset, restore drill, independent security review, ValidationInterval = 0 performance).
