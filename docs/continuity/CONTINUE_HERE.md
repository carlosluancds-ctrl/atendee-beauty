# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Latest verified repository HEAD before V38: `4d33437356b7bdc681a262eb9036e8de22c27c9a` (V37 documentation commit; CI run 44224034308 completed successfully for this exact SHA). V37 implementation commit: `7d681d5da2b8bf58bff8c4b1bd651983639b08ab` (CI run 44223700861). Always re-check `git log`, remote branch SHA and the latest CI instead of trusting this file. See the newest `CONTINUATION_STATUS_V*.md` first.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Desktop Commander processes return after ~3 s; poll with read_process_output. In PowerShell avoid complex gh --jq quoting; use `gh run list` / `gh run view --log | Select-String`. Use absolute paths in .NET file APIs. PowerShell 5 reads BOM-less scripts as ANSI: add a UTF-8 BOM to scripts containing accents.

## Last completed steps
- V38 (documentation step, CI green): added the mandatory versioned continuity-artifact rule; content commit `405b985dcb8054d25e9c37f2ea0eb4961d6bcad6`, CI run 44225192164. See `CONTINUATION_STATUS_V38_2026-11-18.md`.
- V37: trusted actor propagation to trigger-written audit events; migration 008; transaction-local `app.actor_id`; adversarial HTTP and pooled-connection tests. Code commit 7d681d5, green PostgreSQL 18.7 CI run 44223700861. See CONTINUATION_STATUS_V37.
- V35: owner APPROVED the tenant-status access matrix (2026-11-18, "Eu aprovo aquela tabela lá"). Docs only. See CONTINUATION_STATUS_V35.
- V34: administrative actions design, docs only (docs/technical/ADMIN_ACTIONS_DESIGN_V1.md).
- V33: tenant status change audit via DB trigger, migration 007 (57eb581, green CI 44218855206).
- V32: documented commit fa8d606 (tenant-status access policy + authorization matrix). V31/V30/V29: MFA, membership and login audit.

## Decisions status
- APPROVED by owner: tenant-status access matrix (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md). Only the owner can change it.
- STILL OPEN (ask the owner, do not decide): PastDue grace period and PastDue -> Suspended process; post-Closed retention/export/deletion; who/how changes a tenant status; Platform Admin model, step-up window, four-eyes, status transition graph, audit retention, break-glass rules (ADMIN_ACTIONS_DESIGN_V1.md section 10, items 2-6).

## Next step
1. Extend identity-target audit events with a trusted `TargetTenantId`, resolved from persisted membership within the transaction, and test target consistency and grants.
2. Do not expose a tenant-visible audit read view or export endpoint until the owner decides audit visibility, retention and post-Closed handling.
3. Ask the owner for the remaining decisions in `ADMIN_ACTIONS_DESIGN_V1.md` before any status-change flow or Platform Admin work.

## Routine for every step
Verify git status/HEAD/CI -> inspect code -> small change + negative tests -> git diff --check + Release build -> commit/push on the work branch -> watch CI for the exact SHA (confirm new assertions in the log) -> update CONTINUATION_STATUS_V<n> and this file -> clean working tree -> summarize.

## Guardrails (absolute)
No main changes, no deploy, no public signup, no admin disable/revoke endpoints yet, never trust client-sent tenant/role/actor, never log secrets/PII, do not call the audit ledger immutable against DB owner/superuser, CI green != production ready. Do not change the approved status matrix or decide the open items without explicit owner confirmation.

## How to resume in a new account (V36)
- The repo is PRIVATE and its default branch `main` does NOT contain this work. The new account needs GitHub access to the private repo and must use the branch `security/csrf-webhook-antiforgery-v23` (link: https://github.com/carlosluancds-ctrl/atendee-beauty/tree/security/csrf-webhook-antiforgery-v23).
- Suggested message to the new Claude: "Continue esse projeto. Repositório: https://github.com/carlosluancds-ctrl/atendee-beauty, branch security/csrf-webhook-antiforgery-v23. Leia CLAUDE.md e docs/continuity/CONTINUE_HERE.md."
- Entry points added on the branch: root `CLAUDE.md` (resume protocol and guardrails) and a banner at the top of `README.md`. The original long handoff file (ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md) is NOT stored in the repo; its essentials are in CLAUDE.md, this file, the V23-V36 status files and docs/continuity/BACKUP_HANDOFF_2026-11-17.md. Attach the original if you have it.
- Running git/gh commands needs a connected device (Desktop Commander) or another authorized way to run them; without it, read-only analysis and design are still possible.


## Mandatory continuity artifact rule (V38)
Every completed development or documentation step MUST create a new versioned status file:
`docs/continuity/CONTINUATION_STATUS_VNN_YYYY-MM-DD.md`.
Never overwrite prior status files or reuse a version number. Record scope, changed files, migrations, decisions/approvals, exact tests and results, build/CI links and tested SHA, branch/HEAD/worktree/push state, risks, open decisions, and the next safe step. Update this index to point to the newest status while preserving the historical files. Never claim a test, build, push, or CI success without verifying it. A step is not complete until its status file exists and its path is confirmed. If the status artifact cannot be created or updated, explicitly leave documentation pending. At the end, give the owner the exact filename and a usable reference/link. On a new session, read this index and the newest status file before editing code, then repeat this rule for the next step.

Latest verified completed step: V38 content commit `405b985dcb8054d25e9c37f2ea0eb4961d6bcad6`, CI run `44225192164` successful for the exact SHA. The newest status artifact is `docs/continuity/CONTINUATION_STATUS_V38_2026-11-18.md`.

- V39 (TargetTenantId on new identity-target audit events): implementation in progress; see `CONTINUATION_STATUS_V39_2026-11-18.md`. Do not proceed to audit read/export until owner decisions on visibility, retention, and post-Closed behavior are explicit.
