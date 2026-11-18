# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: fa8d6067ab8e7f09b18a16f0363a1372d6df344f (run 44217401209, PostgreSQL 18.7). Docs-only commits may follow it; always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Note: Desktop Commander processes return after ~3 s; poll with read_process_output.

## Last completed steps
- V32 (this file set): documents commit fa8d606 (tenant-status access policy + authorization matrix, green CI run 44217401209). See CONTINUATION_STATUS_V32.
- V31: audit of MFA enrollment failures / password-verified-pending-MFA (94848b1, green). V30: membership-change trigger audit. V29: login outcome audit.

## OPEN POINT (ask the owner first)
The tenant-status access policy (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md) was implemented citing a user delegation that could not be verified from the repo. It is PROVISIONAL until the owner confirms or edits it. Do not build further enforcement on top of it before that. Details in CONTINUATION_STATUS_V32.

## Next step
1. Ask the owner to confirm (or adjust) the tenant-status policy; also still undecided: PastDue grace period, post-Closed retention/export, who changes a tenant status.
2. Meanwhile (safe, independent of the policy): audit of tenant status changes via DB trigger, same pattern as the V30 membership trigger (migration 007, allowlisted event type, CI test with runtime-role grants).
3. Priority C design only (no admin endpoints yet).

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready. The tenant-status policy is provisional (see open point); do not extend it without owner confirmation.
