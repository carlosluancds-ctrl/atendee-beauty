# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest approved checkpoint: `docs/continuity/CONTINUATION_STATUS_V86_2026-11-19.md`.
- Latest fully validated implementation head: `0be194941318e38ee7cbaa2e0fc465eba287c8bd`.
- Latest full CI: run `44633827726` (run 231), success on that exact head. Frontend audit/tests/build passed; portal browser E2E 8/8; locked restore and Release build passed; domain checks 63/63; PostgreSQL 18.7/EF Core/RLS checks 32/32; Identity/TOTP checks 49/49; HTTP authentication/session/security checks passed; real Kestrel + disposable PostgreSQL full-stack browser E2E 1/1. Credentials cleared and disposable PostgreSQL removed.
- V86 implementation commit: `3f6d64b4b6a725ef502767476e50c69c7fd56a2f`; follow-up test correction commit: `0be194941318e38ee7cbaa2e0fc465eba287c8bd`.
- V85 was fully validated before V86: run `44595166252` (run 229), exact head `b68f6feb5869d77f829061f28994029a5ab8b707`; 55 domain checks, 32 PostgreSQL/RLS checks, 49 Identity checks, portal E2E 8/8 and full-stack E2E 1/1 all passed. V85 added recurring weekly professional availability.
- V84 remains the approved business-hours checkpoint. Historical checkpoints V23-V84 are preserved unchanged.
- V84 business hours, V85 recurring professional availability and V86 date-specific time off are configuration/persistence layers only. They are not yet combined into authoritative bookable slots.
- V86 APIs: GET bounded date range, POST full-day/partial-day time off, POST archive. Full-day uses null start/end; partial intervals require both local times, max four per date, no overlap; time off is subtractive only. Owner/Manager write; Owner/Manager/Receptionist read team entries; Professional reads own entries only. RLS, advisory lock, deferred constraint trigger, mandatory audit and soft archive are validated.
- Workspace: use the clean clone `C:\Users\iaras\Downloads\atendee-beauty-v85-work`. The older `C:\Users\iaras\Downloads\atendee-beauty-git` clone has invalid Git metadata and all files appeared untracked; it was left untouched for recovery. Recheck status before editing.
- Next: implement authoritative availability calculation combining V84 salon hours, V85 professional weekly availability, V86 time-off exclusions, service assignment, duration and buffers. Define explicit DST ambiguous/nonexistent local-time handling and test date boundaries. Then implement transactional appointment booking with PostgreSQL overlap prevention, idempotency, immutable snapshots, state transitions and audit.
- In parallel, close shared/distributed authentication rate limiting with multi-instance concurrency tests without assuming a production Redis/managed-service topology. Keep Data Protection certificate rotation blocked until old-key decryption support and a coordinated plan exist.
- Do not deploy, configure SES/email or WhatsApp, activate billing/trial/payment providers, change production data/configuration or modify `main`.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and independent TOTP replay guard.
- Keep A1/A2 administrative endpoints blocked pending explicit owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.

---
