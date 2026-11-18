# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: 57eb581ddd425087f5c05955e56e8ea86ce610a8 (run 44218855206, PostgreSQL 18.7). Docs-only commits follow it (V33 record, V34 design, V35 approval); always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Desktop Commander processes return after ~3 s; poll with read_process_output. In PowerShell avoid complex gh --jq quoting; use `gh run list` / `gh run view --log | Select-String`. Use absolute paths in .NET file APIs. PowerShell 5 reads BOM-less scripts as ANSI: add a UTF-8 BOM to scripts containing accents.

## Last completed steps
- V35: owner APPROVED the tenant-status access matrix (2026-11-18, "Eu aprovo aquela tabela lá"). Docs only. See CONTINUATION_STATUS_V35.
- V34: administrative actions design, docs only (docs/technical/ADMIN_ACTIONS_DESIGN_V1.md).
- V33: tenant status change audit via DB trigger, migration 007 (57eb581, green CI 44218855206).
- V32: documented commit fa8d606 (tenant-status access policy + authorization matrix). V31/V30/V29: MFA, membership and login audit.

## Decisions status
- APPROVED by owner: tenant-status access matrix (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md). Only the owner can change it.
- STILL OPEN (ask the owner, do not decide): PastDue grace period and PastDue -> Suspended process; post-Closed retention/export/deletion; who/how changes a tenant status; Platform Admin model, step-up window, four-eyes, status transition graph, audit retention, break-glass rules (ADMIN_ACTIONS_DESIGN_V1.md section 10, items 2-6).

## Next step
1. Implementation step 1 of the admin design (safe without open decisions): trusted actor propagation to the database (transaction-local setting read by the triggers) with an adversarial test that a client-supplied value never reaches it. No endpoints.
2. Then step 2 (TargetTenantId on identity-target events, tenant-scoped read view, read-only role).
3. Ask the owner for the still-open decisions in one short message before any status-change flow or Platform Admin work.

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA (confirm new assertions in the log) -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready. Do not change the approved status matrix or decide the open items without explicit owner confirmation.
