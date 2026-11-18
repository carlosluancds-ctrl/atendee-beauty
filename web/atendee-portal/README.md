# Atendee Portal — login, MFA and activation frontend

React 19 + TypeScript single-page frontend for the operational identity onboarding flow.

## Local development

Requires Node.js 22 or later.

1. Install locked dependencies: `npm ci`.
2. Set the local ASP.NET API origin and start the portal:

   ```powershell
   $env:ATENDEE_API_PROXY_TARGET = "https://localhost:7045"
   npm run dev
   ```

   Change the proxy target to the actual local API origin. The built-in development server proxies `/api/*` on the same origin so antiforgery cookies, the MFA setup cookie and authentication cookies work without CORS.

3. Open `http://127.0.0.1:4173/login`.

The development proxy accepts the API's local development TLS certificate. It is not used in production.

## Production hosting

Serve the `dist/` directory as static content. Configure IIS URL Rewrite/ARR (or an equivalent reverse proxy) so `/api/*` is forwarded to the ASP.NET API on the same HTTPS origin. Keep the frontend and API same-origin; do not enable permissive CORS. The server must preserve Set-Cookie response headers and forward the X-CSRF-TOKEN request header.

The SPA provides:
- `/activate#token=...` — one-time account activation.
- `/login` — email/password login.
- MFA challenge for accounts with MFA already enabled.
- MFA enrollment for provisioned identities without MFA.
- One-time recovery-code display after successful MFA enrollment.

## Security behavior

- Activation token is read from the URL fragment and removed from visible URL/history before rendering.
- Login, MFA challenge, enrollment and activation use the antiforgery endpoint and X-CSRF-TOKEN header for unsafe requests.
- All API calls use same-origin credentials and no-store fetches.
- No session, activation token, password, MFA secret or antiforgery token is persisted in localStorage/sessionStorage.
- MFA recovery codes are shown once and are held only in component memory until the user confirms they have saved them.
- A pending first Owner completes MFA without receiving an application session; the existing offline V41 command is still required to create the salon membership.
- No permissive CORS, third-party scripts, external fonts or third-party QR service.

## Commands

- `npm test` — Node.js built-in tests for URL-fragment parsing and password pre-validation.
- `npm run build` — strict TypeScript check and production esbuild bundle.
- `npm audit` — dependency vulnerability check.