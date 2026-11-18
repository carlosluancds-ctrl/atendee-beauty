# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account). Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Last verified code SHA with green CI: 418699ddbcf22d198519570a7cc5412a678934cb (run 44213223276, PostgreSQL 18.7). Docs-only commits may follow it; always re-check `git log` and the latest CI instead of trusting this file.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens).

## Last completed step (V29)
Audit coverage for authentication: operational_auth.login_succeeded, operational_auth.login_failed, operational_mfa.challenge_failed (migration 004, writer allowlist, HTTP tests). Details and limitations: CONTINUATION_STATUS_V29_2026-11-18.md.

## Next step (Priority A, continued / B)
1. Audit membership role change / activation / deactivation (trusted, transactional writer path; no tenant ID from client).
2. Audit MFA enrollment failures and the password-verified-pending-MFA event.
3. Then Priority B: documented + tested endpoint authorization matrix for Owner/Manager/Receptionist/Professional.

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, do NOT decide tenant-status access policy (ask the user), never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready.