# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: HEAD (V117 Customer Appointment Cancellation completed and locally validated).
- Latest documentation head: V118 documentation with continuity status file (V117 locally validated).
- Latest full CI: V110 run 296 (`44851428076`), success per manifest. V111-V116 are frontend-only, no CI validation required locally. Local validation: `git diff --check` clean, TypeScript/Web build 0 errors, Release build 0 warnings/0 errors, domain checks 79/79 passed, frontend tests 2/2 passed.
- V118: Documentation commit `33ab52d` - push confirmed (local validation docs).
- V111: Frontend Tenant Member Management UI implemented at `/tenant/members`.
- V112: Frontend Customer Management UI implemented at `/customers`.
- V113: Frontend Appointment Calendar UI implemented at `/appointments`.
- V114: Frontend Appointment Creation Form implemented at `/appointments/new`.
- V115: Frontend Appointment Cancellation implemented at `/appointments` - cancel button on appointment cards with confirmation modal.
- V116: Frontend Appointment Rescheduling implemented at `/appointments` - reschedule button with date/time picker modal.
- V117: Customer Appointment Cancellation - public endpoints at `/api/v1/appointments/customer/{bookingCode}` (commit `42d2760` - push confirmed).

## Completed increments

1. **V111**: Frontend Tenant Member Management - `/tenant/members` route with role change, deactivation, reactivation.
2. **V112**: Frontend Customer Management - `/customers` route with CRUD operations (list, create, edit, archive).
3. **V113**: Frontend Appointment Calendar - `/appointments` route with month view listing and status badges.
4. **V114**: Frontend Appointment Creation Form - `/appointments/new` route with customer selection, service selection, professional assignment, and date/time picker.
5. **V115**: Frontend Appointment Cancellation - Cancel button on appointment cards (PendingConfirmation/Confirmed status) with confirmation modal and reason field.
6. **V116**: Frontend Appointment Rescheduling - Reschedule button on appointment cards with date/time picker modal.
7. **V117**: Customer Appointment Cancellation - Public endpoints for customer self-service cancellation with 24h notice policy, booking code based access.

## Next increments

1. **Customer self-service rescheduling** - Replacement vs history semantics requires product-owner approval.
2. **Customer notification flows** - WhatsApp/email integration requires product, credential, and cost decisions.
3. The transactional outbox has no dispatcher/consumer. Build it only alongside real idempotent handlers, retry/backoff, poison/dead-letter handling, observability and approved provider/credential/cost choices.
4. Data Protection key-ring persistence/protection and certificate rotation remain blocked until old-key compatibility and a coordinated deployment-specific rotation plan exist. Do not change production keys or paths.
5. Production readiness requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key management.
6. Platform Admin and A1/A2 cross-tenant administration remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner and audit requirements.

## Guardrails

- No deploy, no production data/configuration changes, no `main` changes.
- Do not enable public signup or configure email/WhatsApp/AI/billing/trial/payment providers without required product, credential and cost decisions.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, tenant isolation, RLS, transaction runner, mandatory audit, per-identity row locking and TOTP replay protections.
- Never commit/log secrets. The distributed limiter HMAC key must be shared across instances and requires coordinated rotation.
- Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`.
- Preserve the older invalid clone `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.

## Recent Activity

- V117 (2026-11-21): Customer Appointment Cancellation API implemented. Added public endpoints `/api/v1/appointments/customer/{bookingCode}` for GET (view) and POST (cancel). Booking code is base64(tenantId:N + appointmentId:N). 24h notice validation enforced. Phone redacted in responses. Commit: 42d2760 - push confirmed.
- V116 (2026-11-21): Frontend Appointment Rescheduling UI completed. Added cancel and reschedule buttons on appointment cards with separate modals. Reschedule modal has date/time pickers, cancel modal has reason textarea. Features: CSRF protection, authorization checks, loading states.
- V115 (2026-11-21): Frontend Appointment Cancellation UI completed. Added `/appointments` cancel button on appointment cards with confirmation modal and reason field.
- V114 (2026-11-21): Frontend Appointment Creation Form UI completed. Added `/appointments/new` route, `AppointmentFormPage.tsx`, updated types with `Service` and `Professional`, and CSS styles. Features: customer search, service/professional selection, date/time picker, CSRF protected submission.
- V113 (2026-11-21): Frontend Appointment Calendar UI completed. Added `/appointments` route, `AppointmentPage.tsx`, and CSS styles. Displays current month appointments with color-coded status badges.
- V112 (2026-11-21): Frontend Customer Management UI completed. Added `/customers` route, `CustomerPage.tsx`, updated types and styles. Features: list, create, edit, archive with CSRF protection.
- V111 (2026-11-20): Frontend Tenant Member Management UI completed. Added `/tenant/members` route, `TenantMemberPage.tsx`, `types.ts`, and CSS styles. All forms protected by CSRF token. Authorization enforced per TenantMemberEndpoints.cs.