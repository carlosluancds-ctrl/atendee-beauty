# Atendee Beauty - Continuity Status V36 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23. main untouched (44640018f6fa596755d93f9aa0e5affb713f8344). No deploy, no signup.
- Starting point verified: local HEAD = remote HEAD = d9db84d37669268954b4992c23010f4b5761107d, clean tree. Last code SHA with green CI: 57eb581 (run 44218855206).
- This increment is documentation/entry-point only (no code, migration or workflow change).

## Increment: make the repository self-sufficient for resuming in a new account
Gaps found by checking the real repository state:
- The repo is private and its default branch is main, which does not contain the work; a new account given only the link would land on main without the continuity docs.
- README.md was stale (state of 17/11) and did not point to the continuity docs.
- The original long handoff file is not stored in the repo.

Changes (on the work branch only):
- New root CLAUDE.md: where the work is, reading order, state verification, working rules, absolute guardrails, access tips.
- README.md: banner at the top pointing to the work branch, CLAUDE.md and CONTINUE_HERE.md (rest of the README untouched, historical block flagged as outdated).
- CONTINUE_HERE.md: new section "How to resume in a new account" with the suggested message and the access requirements.

## Limitations (cannot be fixed from inside the repo)
- The new account must have GitHub access to the private repo and must be told the branch name (the default branch is main). Making the work branch the default or merging it to main was NOT done: it needs the owner's decision and is outside the current authorization.
- Running git/gh commands requires a connected device (Desktop Commander) or another authorized method.
- The original handoff file is not in the repo; its essentials are in CLAUDE.md, CONTINUE_HERE.md and the status files.

## Verification
- git diff --check clean; README diff is 2 inserted lines, accents preserved. No code changed, so no new build/test claim. CI of the docs commits is recorded when observed.

## Remaining work
Unchanged from V35: implementation step 1 of the admin design (trusted actor propagation to the database with an adversarial test), then step 2; owner decisions still open (PastDue grace, post-Closed retention, who changes status, Platform Admin model, step-up window, four-eyes, audit retention, break-glass).
