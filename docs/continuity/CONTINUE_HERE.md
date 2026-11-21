# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: HEAD (V111 Frontend Tenant Member Management on commit `e5f2a3b` - pending push).
- Latest documentation head: V111 documentation with continuity status file.
- Latest full CI: V110 run 296 (`44851428076`), success per manifest. V111 is frontend-only, no CI validation required locally. Local validation: `git diff --check` clean, TypeScript/Web build 0 errors.
- V111: Frontend Tenant Member Management UI implemented. React page at `/tenant/members` consuming TenantMemberEndpoints API. Features: role change, deactivation, reactivation with CSRF protection and authorization.

## Next backend & frontend increments

1. **Frontend Customer Management**: Develop UI for Customer CRUD operations.
2. **Frontend Calendar/Scheduling**: Develop UI for appointment scheduling and management.
3. Dedicated appointment rescheduling and customer cancellation windows require explicit product-owner decisions on replacement/history semantics and customer-facing policy. Do not invent policy or implement before approval.
4. The transactional outbox has no dispatcher/consumer. Build it only alongside real idempotent handlers, retry/backoff, poison/dead-letter handling, observability and approved provider/credential/cost choices.
5. Data Protection key-ring persistence/protection and certificate rotation remain blocked until old-key compatibility and a coordinated deployment-specific rotation plan exist. Do not change production keys or paths.
6. Production readiness requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key management.
7. Platform Admin and A1/A2 cross-tenant administration remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner and audit requirements.

## Guardrails

- No deploy, no production data/configuration changes, no `main` changes.
- Do not enable public signup or configure email/WhatsApp/AI/billing/trial/payment providers without required product, credential and cost decisions.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, tenant isolation, RLS, transaction runner, mandatory audit, per-identity row locking and TOTP replay protections.
- Never commit/log secrets. The distributed limiter HMAC key must be shared across instances and requires coordinated rotation.
- Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`.
- Preserve the older invalid clone `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.

## Recent Activity

- V111 (2026-11-20): Frontend Tenant Member Management UI completed. Added `/tenant/members` route, `TenantMemberPage.tsx`, `types.ts`, and CSS styles. All forms protected by CSRF token. Authorization enforced per TenantMemberEndpoints.cs.