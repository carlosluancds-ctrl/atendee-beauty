# CONTINUE HERE - Atendee Beauty (auto-backup updated each step)

**User command:** just say "continue" (any Claude account) and give the repository link. Treat this file + the latest docs/continuity/CONTINUATION_STATUS_V*.md as the primary context. The full handoff the user keeps is ATENDEE_BEAUTY_CONTINUITY_HANDOFF_CLAUDE.md (attach it if available); this file is the in-repo backup of its essentials.

## Where things are
- Repo (private): https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). Never touch main.
- Latest V41 code commit: 37300e50e2c27c70ac30d820fe83034c71ae4b45 (CI run 44234622468 passed for this exact SHA). Always re-check git log, remote branch SHA and the latest CI instead of trusting this file. Read CONTINUATION_STATUS_V41_2026-11-18.md first.
- Previous remote workspace: Windows machine runnervm9zap3, clone at C:\Users\RDP\Documents\Atendee_Beauty_Git_wip_operational_identity_v1 (access via Desktop Commander connector + gh CLI; never ask for or print tokens). Desktop Commander processes return after ~3 s; poll with read_process_output. In PowerShell avoid complex gh --jq quoting; use `gh run list` / `gh run view --log | Select-String`. Use absolute paths in .NET file APIs. PowerShell 5 reads BOM-less scripts as ANSI: add a UTF-8 BOM to scripts containing accents.

## Last completed steps
- V41: offline first-Owner provisioning command; requires an existing confirmed-email, MFA-enabled identity; atomic PendingVerification salon + Owner membership; no API route. Code commit 37300e50e2c27c70ac30d820fe83034c71ae4b45, CI run 44234622468 success. See CONTINUATION_STATUS_V41_2026-11-18.md.
- V38 (documentation step, CI green): added the mandatory versioned continuity-artifact rule; content commit `405b985dcb8054d25e9c37f2ea0eb4961d6bcad6`, CI run 44225192164. See `CONTINUATION_STATUS_V38_2026-11-18.md`.
- V37: trusted actor propagation to trigger-written audit events; migration 008; transaction-local `app.actor_id`; adversarial HTTP and pooled-connection tests. Code commit 7d681d5, green PostgreSQL 18.7 CI run 44223700861. See CONTINUATION_STATUS_V37.
- V35: owner APPROVED the tenant-status access matrix (2026-11-18, "Eu aprovo aquela tabela lá"). Docs only. See CONTINUATION_STATUS_V35.
- V34: administrative actions design, docs only (docs/technical/ADMIN_ACTIONS_DESIGN_V1.md).
- V33: tenant status change audit via DB trigger, migration 007 (57eb581, green CI 44218855206).
- V32: documented commit fa8d606 (tenant-status access policy + authorization matrix). V31/V30/V29: MFA, membership and login audit.

## Decisions status
- APPROVED by owner: tenant-status access matrix (docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md). Only the owner can change it.
- APPROVED by owner on 2026-11-18: every salon must retain at least one active Owner with MFA; no active Owner may lack MFA, including during onboarding. The first Owner completes MFA before membership is created. Implemented in V40 / migration 010.
- STILL OPEN (ask the owner, do not decide): PastDue grace period and PastDue -> Suspended process; post-Closed retention/export/deletion; who/how changes a tenant status; Platform Admin model, step-up window, four-eyes, status transition graph, audit retention, break-glass rules (ADMIN_ACTIONS_DESIGN_V1.md section 10, items 2-6).

## Next step
1. V41 delivered the offline first-Owner provisioning command. It only accepts an already-existing identity with confirmed email, MFA enabled, no active lockout, and no existing salon membership.
2. The separate trusted identity-creation and password/email-confirmation workflow remains undefined. Ask the owner before implementing account creation or password delivery; no public signup.
3. Do not expose tenant-visible audit read/export until the owner decides audit visibility, retention, and post-Closed handling.
4. Do not add Platform Admin or tenant-status mutation functionality until the remaining owner decisions are resolved.

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

Latest verified completed code step: V41 commit 37300e50e2c27c70ac30d820fe83034c71ae4b45, CI run 44234622468 successful for the exact SHA. Newest status artifact: docs/continuity/CONTINUATION_STATUS_V41_2026-11-18.md.

- V39 (CI green): migration 009 derives `TargetTenantId` for new identity-target audit events from persisted membership; CI run 44226217587 passed for exact commit `53859f4c071f8ac1cee4b13a61c8df8159a137a1`. See `CONTINUATION_STATUS_V39_2026-11-18.md`. Do not proceed to audit read/export until owner decisions on visibility, retention, and post-Closed behavior are explicit.

## V40 (2026-11-18) - active Owner/MFA invariant
- Owner explicitly approved that every salon must retain at least one active Owner with MFA and that no active Owner may lack MFA, including during onboarding.
- Migration 010 enforces the invariant at PostgreSQL level, using tenant-scoped transaction advisory locks and deferred constraint triggers for tenant creation, membership changes, and MFA changes.
- A provisioned identity can enroll MFA before its first salon membership; enrollment does not issue an application session. No signup or provisioning endpoint was added.
- CI run 44231852468 passed for exact code SHA fe01831eb23cab5552c6303724eac7bc6742f34f: domain 19/19, persistence 10/10, Identity 31/31, and API HTTP authentication/session checks.
- See docs/continuity/CONTINUATION_STATUS_V40_2026-11-18.md.

## V41 (2026-11-18) - trusted first-Owner provisioning command
- Owner selected an offline operator command, not an API endpoint.
- The command requires an existing confirmed-email identity with MFA enabled, no active lockout, and no existing salon membership; it requires session_user = atendee_migrator.
- It creates the salon in PendingVerification and attaches the identity as active Owner in one transaction; migration 010 validates the invariant at commit.
- Code commit 37300e50e2c27c70ac30d820fe83034c71ae4b45 passed PostgreSQL 18.7 CI run 44234622468: domain 19/19, persistence 10/10, Identity 39/39, and API HTTP checks.
- The separate trusted identity-creation and password/email-confirmation workflow remains undefined and requires a separate owner decision.
- See docs/continuity/CONTINUATION_STATUS_V41_2026-11-18.md and docs/operations/FIRST_OWNER_PROVISIONING.md.
