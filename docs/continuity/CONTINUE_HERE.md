# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** this file plus the highest-numbered `docs/continuity/CONTINUATION_STATUS_V*.md`. Read `CLAUDE.md`, this file, and the newest status file before any code change.

## Repository and branch
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Active work branch: `security/csrf-webhook-antiforgery-v23`. Never modify `main`.
- At V42 documentation checkpoint, the remote code baseline was `5e37dbbcc23ddd40a7cf478b2b2947ad74bfcba5`; V42 adds a documentation-only commit on this branch.
- Latest code CI before V42: run 44235109072 passed for code SHA `5e37dbbcc23ddd40a7cf478b2b2947ad74bfcba5`: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44235109072
- Re-check remote branch SHA, latest CI and local worktree before every implementation increment. The local Windows clone was not accessible during V42.

## Completed work
- V41: offline first-Owner salon provisioning command. It requires an existing confirmed-email identity with MFA enabled, no active lockout and no existing salon membership; creates PendingVerification salon + active Owner membership atomically. No HTTP route. See `CONTINUATION_STATUS_V41_2026-11-18.md` and `docs/operations/FIRST_OWNER_PROVISIONING.md`.
- V42: owner approved a separate protected operator command to create the initial operational identity and initiate activation. Decision only; no code, email provider, endpoint, or deployment added. See `CONTINUATION_STATUS_V42_2026-11-18.md`.

## Decisions and boundaries
- Approved: first operational identity creation via a separate protected operator command that starts activation.
- Not approved: public self-registration, public salon provisioning, or account-creation endpoint.
- V41's salon provisioning command remains separate and must not create identities/passwords.
- The new identity must confirm email, set its password, and complete pre-membership MFA before V41 can attach the Owner membership.
- Outbound email provider remains unselected. Do not implement real email delivery or configure provider credentials until the owner chooses one.
- Use ASP.NET Core Identity; never implement password hashing directly.
- Never place passwords, activation tokens, email-confirmation tokens, TOTP secrets or recovery codes in CLI arguments, logs, console output, CI logs or audit events.
- Any application-managed activation token must be high entropy, single use, expiring, bound to the intended identity/email, and stored only as a cryptographic hash.
- Preserve all other V41 security/product guardrails and unresolved product decisions.

## Next step
Ask the owner which transactional email provider to use for activation delivery. Once selected, inspect official integration and secret-management requirements, then present the specific implementation plan before changing code.

## Mandatory continuity artifact rule
At the end of every completed code or documentation step:
1. Create a new `docs/continuity/CONTINUATION_STATUS_VNN_YYYY-MM-DD.md` using the next unused version number; never overwrite old status files.
2. Update this index and `docs/continuity/manifest.json`.
3. Commit/push directly to this work branch and verify the commit and file paths.
4. Record exact tests/build/CI and SHA only when actually observed. For docs-only changes, explicitly state that code tests/build were not run.
5. Preserve all previous status files and never change `main`.

## Absolute guardrails
No main changes, no deployment, no public signup, no client-trusted tenant/role/actor, no secrets in logs, no unapproved Platform Admin or tenant-status mutation features. CI green is not production approval.
