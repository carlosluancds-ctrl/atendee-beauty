# Atendee Portal — activation frontend

React + TypeScript single-page frontend for the operator-issued account activation flow.

## Local development

Requires Node.js 22 or later.

1. Install locked dependencies: `npm ci`.
2. Set the local ASP.NET API origin and start the portal:

   ```powershell
   $env:ATENDEE_API_PROXY_TARGET = "https://localhost:7045"
   npm run dev
   ```

   Change the proxy target to the actual local API origin. The built-in development server proxies `/api/*` on the same origin so the antiforgery cookie and request header work without CORS.

3. Open `http://127.0.0.1:4173`.

The development proxy accepts the API's local development TLS certificate. It is not used in production.

## Production hosting

Serve the `dist/` directory as static content. Configure IIS URL Rewrite/ARR (or an equivalent reverse proxy) so `/api/*` is forwarded to the ASP.NET API on the same HTTPS origin. Keep the activation page and API same-origin; do not enable permissive CORS. The server must preserve the antiforgery `Set-Cookie` response and forward the `X-CSRF-TOKEN` request header.

## Activation security behavior

- Reads the one-time token from the URL fragment and removes the fragment from the visible URL/history before rendering.
- Requests `GET /api/v1/auth/antiforgery` to obtain the request token and antiforgery cookie.
- Posts `{ token, password }` to `POST /api/v1/auth/activate` with `X-CSRF-TOKEN`, same-origin credentials, and no-store fetches.
- Does not store the activation token, password, or antiforgery token in localStorage/sessionStorage.
- Does not issue or assume a signed-in session after activation; the user must sign in separately and complete MFA enrollment.

## Commands

- `npm test` — Node.js built-in tests for URL-fragment parsing and password pre-validation.
- `npm run build` — strict TypeScript check and production esbuild bundle.
- `npm audit` — dependency vulnerability check.