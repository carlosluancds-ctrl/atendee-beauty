# PostgreSQL 18.7 isolated RLS test harness

**Purpose:** execute the RLS proof of concept in a disposable PostgreSQL 18.7 container. This is a lab harness, not production deployment.

## Requirements

- A separate development host or CI runner with Linux Docker Engine and Docker Compose v2.
- `openssl` for generating a temporary random password.
- Internet access to pull the official `postgres:18.7-bookworm` image.

## Run

From the starter-kit root:

```bash
bash ./scripts/test-rls.sh
```

The script:

1. Refuses to run when Docker is in Windows-container mode.
2. Generates a random password only in the process environment.
3. Starts a uniquely named PostgreSQL 18.7 container.
4. Publishes no database port to the host.
5. Creates a disposable database and the two POC roles.
6. Runs both SQL scripts with `ON_ERROR_STOP`.
7. Removes the container and its volumes on exit, including on test failure.

No `.env` file or persistent project secret is created.

## Tests included

- Runtime role is `NOLOGIN`, `NOINHERIT`, `NOSUPERUSER`, `NOBYPASSRLS`.
- Runtime is not a member of the table-owner role.
- The tenant table has RLS enabled and forced and exactly one policy in this isolated schema.
- No tenant context gives no visible rows and blocks inserts.
- Tenant A cannot read, insert, update, delete or reassign rows belonging to tenant B, even with known UUIDs.
- Tenant A can update/delete its own row.
- A new transaction in the same session receives no previous transaction-local tenant context.

## Limitations

- The test uses a `NOLOGIN` runtime role via `SET LOCAL ROLE` from the disposable cluster administrator. This verifies row-policy behavior for that role; it is not yet an application connection-pool integration test.
- The custom GUC `app.tenant_id` is settable by a session with SQL access. This POC does not protect against SQL injection or arbitrary SQL executed under the runtime role. The backend must derive tenant identity from authenticated server-side state and use parameterized queries. RLS is defense in depth, not a substitute for application security.
- This suite does not yet exercise EF Core/Npgsql, connection-pool reuse across HTTP requests, API authorization, caching, jobs, file storage or restore procedures. Those need later integration tests.

## Important for the current Windows Server

The current host's Docker engine was observed in Windows-container mode (`OSType=windows`) and WSL2/Linux container support is not available. This script intentionally exits without changing Docker mode. Run it on a separate Linux development/CI host. Do not switch the production server's Docker engine mode to run this test.

The server also has PostgreSQL 14.24 binaries and a stopped/disabled service. PostgreSQL 14 reached end-of-life on 12 November 2026; do not use that installation for this project. It has not been started, changed or removed.
