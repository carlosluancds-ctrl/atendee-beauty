# CONTINUE HERE - Atendee Beauty

Read CLAUDE.md and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: security/csrf-webhook-antiforgery-v23; never modify main.
- Latest approved checkpoint: docs/continuity/CONTINUATION_STATUS_V87_2026-11-19.md.
- Latest fully validated implementation head: 8e0b70e66bf59833a7474fd6f9a8ef7b0476afee.
- Latest full CI: run 44638633076 (run 234), success on that exact head. Frontend audit/tests/build passed; portal browser E2E 8/8; locked restore and Release build passed; domain checks 69/69; PostgreSQL 18.7/EF Core/RLS checks 32/32; Identity/TOTP checks 49/49; HTTP availability/authentication/session/security checks passed; real Kestrel + disposable PostgreSQL full-stack browser E2E 1/1. Credentials cleared and disposable PostgreSQL removed.
- V87 implementation commit: 2833e52aeddb063bfb85e16ead1c8f89fbb257ab; test correction commit: 8e0b70e66bf59833a7474fd6f9a8ef7b0476afee.
- V86 CI remains approved at run 44633827726; V85 remains approved at run 44595166252. Historical checkpoints V23-V86 are preserved unchanged.
- V87 API: GET /api/v1/availability?from=YYYY-MM-DD&to=YYYY-MM-DD&serviceId={id}&professionalId={optional}. Date range is inclusive and limited to 31 local calendar days. Returns UTC earliest/latest service-start windows at one-minute precision.
- V87 combines tenant business hours, professional recurring weekly availability, date-specific full/partial time off, active service assignment, service duration and buffers. DST gaps are skipped naturally; both ambiguous wall-time occurrences remain distinct UTC instants. Owner/Manager/Receptionist can read team availability; Professional can read own only.
- V87 does not yet subtract existing appointments. Do not claim an open window is reservable until V88 booking persistence and PostgreSQL overlap enforcement are implemented.
- Next: V88 transactional appointment creation and reads, immutable service/price/duration/buffer/customer/professional snapshots, idempotency key + request hash, PostgreSQL advisory lock and deferred overlap constraint, append-only appointment events and transactional outbox. Then appointment lifecycle, check-in, in-service, completion, no-show, cancellation and atomic rescheduling. Availability must then subtract active appointments.
- Later: internal conversations/handoff and durable outbox worker. Keep email, WhatsApp, AI provider, billing/payment, Platform Admin and legal retention decisions blocked until their required product, credential, cost or approval decisions are made.
- Workspace: use the clean clone C:\Users\iaras\Downloads\atendee-beauty-v85-work. The older C:\Users\iaras\Downloads\atendee-beauty-git clone has invalid Git metadata and all files appeared untracked; it was left untouched for recovery. Recheck status before editing.
- Do not deploy, configure SES/email or WhatsApp, activate billing/trial/payment providers, change production data/configuration or modify main.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner, audit-before-commit/cookie ordering, per-identity row locking and independent TOTP replay guard.
- Keep A1/A2 administrative endpoints blocked pending explicit owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements.

---