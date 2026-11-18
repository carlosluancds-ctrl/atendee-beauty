# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest checkpoint: `docs/continuity/CONTINUATION_STATUS_V60_2026-11-18.md`.
- V59 full CI passed: run 44299982400.
- V60 adds tenant-scoped eligible Professional membership discovery for Owner/Manager and a Professional-only endpoint returning assigned service details.
- V60 HTTP integration checks cover candidate filtering, profile creation removal from candidates, own assigned services and forbidden candidate discovery for Professional/Receptionist.
- V60 full CI for the final branch HEAD is pending; verify complete PostgreSQL 18.7, RLS and HTTP checks before marking approved.
- Browser E2E against the real API remains to be done.
- Local `C:\Temp\atendee-work` is stale. Use GitHub branch and CI.
- Preserve V41 first-Owner provisioning. Do not deploy, configure SES or activate billing/trial without explicit authorization.
