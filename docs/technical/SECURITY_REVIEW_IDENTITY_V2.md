# Security review — operational identity and trusted tenant context (V2)

**Review date:** 2026-11-17  
**Source snapshot:** backup v22-identity-ci-passed  
**Expected upstream branch:** wip/operational-identity-v1  
**Expected branch head:** 6a9d1795b4c30238c1ad1399b0298765f9b4136a  
**Previously CI-tested code commit:** c815881200ea29831a6814aa45917fdf1cac3470  
**Review scope:** static review of API authentication, TOTP, tenant membership resolver, PostgreSQL migration, CI workflow; local restore/build/domain check on extracted backup.

## Local validation performed

- Locked restore: PASS (all seven solution projects restored with `dotnet restore Atendee.sln --locked-mode`).
- Release build before the CSRF hardening edit: PASS, 0 warnings, 0 errors.
- Domain checks: PASS, 15/15.
- Release build after the CSRF hardening edit: PASS, 0 warnings, 0 errors (2026-11-17; local extracted snapshot).
- PostgreSQL persistence and identity integration checks: NOT RUN. Docker engine returned named-pipe permission denied; local PostgreSQL 14 service is stopped/disabled; no psql client was available.
- GitHub Actions re-run: NOT POSSIBLE in this environment. GitHub CLI reports no authenticated GitHub host.
- The extracted ZIP has no .git directory. No branch checkout, commit, push, PR, merge, or remote repository mutation was performed.

## Finding addressed in the local snapshot

### V2-01 — Broad path-prefix exemption from antiforgery (High, preventive hardening)

**Observed:** the global unsafe-method antiforgery middleware excluded every request whose path started with `/api/v1/webhooks`. That exemption was based only on a URL prefix and did not itself require endpoint-specific webhook signature verification.

**Risk:** any future unsafe endpoint accidentally mounted below that prefix would bypass CSRF validation even if it did not validate a provider signature. The exception was broader than the actual trust boundary.

**Change made:** removed the blanket path-prefix exemption. All unsafe HTTP methods now require antiforgery validation. The code comment records that future webhook endpoints do not yet exist and must receive a narrowly scoped, endpoint-specific exception only when signature verification is implemented and tested.

**Compatibility:** no webhook endpoints are currently mapped by the API. A future webhook implementation must add an explicit signature-validation design and tests before it can be exempted from browser antiforgery.

**Validation:** post-change Release build must pass; HTTP tests for ordinary unsafe methods and a future signed webhook endpoint remain required.

## Remaining security blockers

1. **HTTP end-to-end coverage absent:** no API integration-test project currently exists in the solution. Add tests for cookie issuance/attributes, antiforgery, MFA enrollment/challenge/recovery, logout, rate limits, generic login failures, and session revocation.
2. **Session/account revocation incomplete:** membership deactivation is checked on each authenticated request, but there is no complete administrative account-disablement/session-revocation workflow or verified all-session invalidation path. Define security-stamp/session-version semantics and test immediate revocation.
3. **Tenant status authorization matrix incomplete:** login rejects inactive membership and Closing/Closed, while other statuses can currently pass authentication. Before business endpoints, implement an explicit deny-by-default policy for PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing, and Closed; decide narrowly scoped billing/export exceptions.
4. **Role authorization matrix not implemented:** `/me` is the only operational read endpoint shown in the current API mapping. Implement named policies/permissions before business routes; test every role against every endpoint and resource ownership.
5. **Audit pipeline absent:** authentication, MFA changes, account disablement, role changes, session revocation, and security-sensitive actions need structured, tamper-resistant audit events with secrets/PII excluded.
6. **MFA enrollment abuse controls:** current MFA rate limiting is per-IP and in-memory. Add account/session-bound throttling and distributed enforcement before multi-instance deployment; test replay, concurrent verification, recovery-code reuse, and reset/re-enrollment.
7. **Trusted proxy and distributed rate limiting:** do not trust forwarded headers until explicit trusted proxy/network configuration is set. Replace in-memory partitions for multi-instance deployment.
8. **Data Protection deployment controls:** persistent key ring and platform-specific protector are still required; non-Windows production must remain fail-closed until certificate/KMS/managed protection and restore/rotation are proven.
9. **Webhook future boundary:** never exempt a route by path prefix. Require raw-body signature verification, timestamp/replay protection, provider-to-tenant mapping on the server, idempotency, and endpoint-specific antiforgery metadata.
10. **Independent review and production gates:** no real tenant data, public signup, production deployment, or merge to main until critical/high findings and the security acceptance plan are closed.

## Architecture observations

- The operational Identity store is separate from the future Platform Admin boundary.
- The membership resolver uses a transaction-local identity GUC and a fixed SECURITY DEFINER function; the auth login role has no direct SELECT on tenant membership tables.
- PostgreSQL custom GUCs are not a defense against arbitrary SQL execution. Parameterized SQL and prevention of SQL injection remain mandatory.
- Owner/Manager MFA is enforced again on each request using the persisted membership role and signed `amr=mfa` claim.
- TOTP replay state is stored separately and updated atomically; retain the PostgreSQL integration tests as a required gate.
- The existing CI integration programs intentionally fail when their required disposable database connection strings are missing. Do not report a SKIP as a passing integration test.

## Next executable sequence

1. Finish and record the post-edit Release build.
2. Restore Git metadata by authenticating to the private repository and checking out the exact expected branch/head; do not initialize a replacement Git history from the ZIP.
3. Run the PostgreSQL 18.7 workflow in CI, with all persistence and identity checks required to PASS and no SKIP.
4. Add a dedicated ASP.NET Core HTTP integration test project using a disposable PostgreSQL database; test the complete cookie/CSRF/MFA/session behavior.
5. Implement immediate session/account revocation, tenant-status policies, role authorization, and audit events in small reviewed changes.
6. Re-run all gates, update the handoff, and only then propose a PR to main.

## Operational safety

No production services, firewall, ports, volumes, databases, real user accounts, secrets, or deployment targets were changed. No production deployment was authorized or performed.
