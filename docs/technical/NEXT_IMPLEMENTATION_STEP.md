# Next implementation step — Identity and trusted tenant context

**Persistence gate passed:** GitHub Actions run 44166541043 on commit 6925d2a completed successfully. Locked restore, Release build, 15/15 domain checks, PostgreSQL 18.7 provisioning and 9/9 EF Core/RLS/pool checks passed; the disposable database was removed.

1. Add the operational identity store using ASP.NET Core Identity. Never implement password hashing directly.
2. Keep global Atendee platform-administrator identities in a separate model/store boundary.
3. Require MFA for Owner and Manager. Start with the built-in Identity TOTP provider and one-time recovery codes; keep passkey/WebAuthn as a later enhancement.
4. Use encrypted Secure, HttpOnly, SameSite cookies with CSRF protection; do not issue bearer tokens into browser localStorage.
5. Resolve the active tenant and role from the authenticated user's single server-side membership on each request. Ignore tenant_id in browser inputs, headers and URLs.
6. Revoke access when the account/membership is disabled or its role changes. Test session expiry/revocation, MFA bypass, generic login errors, enumeration and cross-tenant access.
7. Keep signup, email verification and employee invitation delivery closed until an email provider and operational workflow are selected. Tokens must be high-entropy, single-use, stored as hashes and excluded from logs.
8. Extend PostgreSQL CI with identity schema and authentication/tenant-resolution tests. Do not deploy before the full security plan, backup/restore and release gates pass.

No production deployment or production-server changes are authorized by this checkpoint.
