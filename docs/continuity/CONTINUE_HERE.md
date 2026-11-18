# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: 57eb581ddd425087f5c05955e56e8ea86ce610a8 (run 44218855206, PostgreSQL 18.7). Docs-only commits follow it (V33 record, V34 design); always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Desktop Commander processes return after ~3 s; poll with read_process_output. In PowerShell avoid complex gh --jq quoting; use `gh run list` / `gh run view --log | Select-String`. Use absolute paths in .NET file APIs.

## Last completed steps
- V34: administrative actions design, docs only (docs/technical/ADMIN_ACTIONS_DESIGN_V1.md). See CONTINUATION_STATUS_V34.
- V33: tenant status change audit via DB trigger, migration 007 (57eb581, green CI 44218855206).
- V32: documented commit fa8d606 (tenant-status access policy + authorization matrix). V31/V30/V29: MFA, membership and login audit.

## OPEN POINTS (ask the owner first)
1. The tenant-status access policy (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md) was implemented citing a user delegation that could not be verified from the repo. PROVISIONAL until the owner explicitly approves or edits it. A generic "do everything professionally" instruction is not treated as approving that table.
2. Decisions listed in ADMIN_ACTIONS_DESIGN_V1.md section 10 (Platform Admin model, step-up window, four-eyes, status transition graph and PastDue grace, audit retention, break-glass rules).

## Next step
1. Ask the owner for the decisions above (one short message listing them).
2. Safe without those decisions: implementation step 1 of the design, trusted actor propagation to the database (transaction-local setting read by the triggers) with an adversarial test that a client-supplied value never reaches it. No endpoints.
3. Then step 2 (TargetTenantId on identity-target events, tenant-scoped read view, read-only role).

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA (confirm new assertions in the log) -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready. The tenant-status policy is provisional; do not extend it without explicit owner confirmation.
