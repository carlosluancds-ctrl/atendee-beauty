# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account). Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: 4958b13140f817650e81c93102ffc67e290c881b (run 44214087871, PostgreSQL 18.7). Docs-only commits may follow it; always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens).

## Last completed step (V30, green CI run 44214087871 at 4958b13)
Database-trigger audit of membership created/role_changed/activated/deactivated (migration 005). Before it: V29 auth outcome audit (green CI at 418699d). Details: CONTINUATION_STATUS_V29 / V30.

## Next step
1. Audit MFA enrollment failures and the password-verified-pending-MFA event.
2. Priority B: documented + tested endpoint authorization matrix for Owner/Manager/Receptionist/Professional.
3. Priority C design only after A and B.

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, do NOT decide tenant-status access policy (ask the user), never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready.