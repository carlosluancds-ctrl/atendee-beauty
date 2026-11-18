# Atendee Beauty — Continuity Status V64 (in progress, 2026-11-18)

## Current state

V64 has started on top of the approved V63 full-stack browser harness. The new browser regression checks exercise antiforgery request-token binding across identity transitions:

- A token obtained while anonymous must be rejected after the Owner authenticates.
- A token obtained while the Owner is authenticated must be rejected after logout.
- Both negative requests expect HTTP 400 with `antiforgery_validation_failed`.
- The regular Owner service-creation flow continues afterward using the portal's fresh token behavior.

Implementation commits:
- `b27b4030bb8f8a44a6184d1fc8acaac3f8b6c8f9` — add the two session-transition assertions and browser helpers.
- `aa38361ed16017a9642145400a94d9b3a4088286` — correct the logout URL matcher in the new test.

## CI status — not approved

The latest complete approved baseline remains V63:

- V63 implementation run 44319685006: success.
- V63 continuity checkpoint run 44320319677: success.
- V64 runs 44321104904 and 44321314420 failed immediately before any job steps were reported. GitHub's jobs endpoint returned `steps: null`, and the job-log endpoint returned 404. A failed-job retry of run 44321314420 produced the same no-step/no-log result.
- Therefore these V64 failures do not provide a test assertion or compiler diagnostic. They appear to be runner/job-start failures, but the cause is not established. Do not describe V64 as passing.
- The current V64 browser test has not yet been validated by a complete CI run.

## Next actions

1. Restore a diagnosable GitHub Actions execution for the current branch head.
2. Run the complete workflow and inspect the actual Playwright/API result for both stale-token assertions.
3. Fix any real test or implementation failures; do not suppress or mock antiforgery validation.
4. Only mark V64 approved after the complete workflow is green.

## Approved baseline and safeguards

- Latest approved implementation: V63, commit `e106c5926b93168ca4e0c9b7b2a6e762fc6606a4`.
- V63 full-stack browser harness uses real ASP.NET Core Kestrel, the portal, and disposable PostgreSQL 18.7; 8 mocked browser tests remain separate.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after session transitions, V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; `main` remains untouched.
- No production deployment, SES configuration, billing/trial activation or production data changes are authorized.
