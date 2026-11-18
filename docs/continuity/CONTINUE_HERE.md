# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: 57eb581ddd425087f5c05955e56e8ea86ce610a8 (run 44218855206, PostgreSQL 18.7). Docs-only commits may follow it; always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Desktop Commander processes return after ~3 s; poll with read_process_output. In PowerShell avoid complex gh --jq quoting; use `gh run list` / `gh run view --log | Select-String`.

## Last completed steps
- V33: audit of tenant status changes via DB trigger, migration 007 (57eb581, green CI 44218855206). See CONTINUATION_STATUS_V33.
- V32: documented commit fa8d606 (tenant-status access policy + authorization matrix). See CONTINUATION_STATUS_V32.
- V31: MFA enrollment failure / pending-MFA audit. V30: membership-change trigger audit. V29: login outcome audit.

## OPEN POINT (ask the owner first)
The tenant-status access policy (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md) was implemented citing a user delegation that could not be verified from the repo. It is PROVISIONAL until the owner confirms or edits it. Do not build further enforcement on top of it before that. Also undecided: PastDue grace period and PastDue -> Suspended process, post-Closed retention/export/deletion, who/how changes a tenant status.

## Next step
1. Ask the owner to confirm (or adjust) the tenant-status policy and the undecided items above.
2. Priority C, design only: a design document (docs/technical/) for administrative actions: disable/revoke, membership management, audit review/export, Platform Admin and break-glass, each with identity, authorization, scope, MFA and audit requirements. No endpoints.
3. Optional hardening: stricter insert path for trigger-written audit events (see V33 limitations); then the other security items listed in V33 "Remaining work".

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA (confirm new assertions in the log) -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready. The tenant-status policy is provisional (see open point); do not extend it without owner confirmation.
