# Security boundaries in the starter kit

- There is no authentication middleware yet. Do not deploy this API publicly.
- There are no business endpoints yet.
- Tenant context is an application abstraction only; it has no HTTP implementation.
- There is no database connection or RLS migration in the runtime application.
- The platform administrator identity is intentionally not modeled as a tenant user.
- Do not enable CORS until exact frontend origins are known.
- Do not configure forwarded headers until trusted proxy/load-balancer addresses are known.
- Do not add production secrets to configuration files.
- The SQL files in `db/poc` are disposable lab scripts, not production migrations.
- The `app.tenant_id` custom GUC can be set by any SQL session; never mistake this POC for SQL-injection protection.

The health endpoint is a scaffold convenience, not a production readiness check.
