# Architecture and API design

This is the proposed contract. Only GET /api/health is implemented in this preparation batch. Runtime Swagger describes implemented endpoints only.

## Authentication
Use ASP.NET Core Identity with EF Core and PostgreSQL through the Npgsql provider. Use PostgreSQL in development, integration tests, and production. Identity manages password hashing, account validation, and lockout. Local development uses the official PostgreSQL 18 image in Docker Compose with a persistent named volume. Keep credentials outside git. EF Core migrations will version the account and log schema; review and apply migrations explicitly rather than automatically migrating during production startup.
Use HttpOnly cookie sessions for the browser, SameSite=Lax and Secure in production. Serve the UI and /api from the same origin in production; Vite proxies /api locally. Do not store session tokens in localStorage.
Implement explicit registration/login/logout wrappers around Identity rather than exposing the entire Identity API surface. Login failures must be generic and rate limited; do not reveal account existence. Configure email confirmation and password reset delivery before public release.
Issue an antiforgery token from GET /api/auth/csrf and require X-CSRF-TOKEN on all unsafe browser requests, including registration/login/logout. Cookies alone are insufficient CSRF protection. Return API 401/403 responses rather than redirecting to HTML login pages.

## Routes
| Route | Request | Result | Access |
| --- | --- | --- | --- |
| GET /api/health | — | 200 {status: "ok"} | Public; process health only |
| GET /api/auth/csrf | — | 200 {token: string} + antiforgery cookie | Public |
| POST /api/auth/register | {email, password} | 201; no automatic sign-in | Public + CSRF |
| POST /api/auth/login | {email, password} | 204 + session cookie | Public + CSRF |
| POST /api/auth/logout | — | 204, removes session | Signed in + CSRF |
| GET /api/auth/me | — | 200 {id, email} | Signed in |
| POST /api/logs | {message, level, occurredAt} | 201 log, Location header | Signed in + CSRF |
| GET /api/logs?page=1&pageSize=25&level=info | — | 200 {items, page, pageSize, total} | Signed in |

Log responses contain id, message, level, occurredAt, createdAt; omit ownerId. UTC timestamps use ISO 8601. Defaults: page=1/pageSize=25; page >= 1; pageSize 1–100. Optional level is info/warning/error. Reject unsupported level values and invalid timestamps. Sort by occurredAt descending, then id for stable ordering. No GET-by-ID endpoint is planned yet; Location points to the filtered list resource until detail routes exist.
Errors: 400 validation Problem Details, 401 unauthenticated, 403 CSRF/forbidden, 409 generic registration conflict, 429 rate limited. Never return exception details in production.

## Layer boundaries
Domain: LogEntry and invariants. Application: log use cases, DTOs, repository/current-user abstractions. Infrastructure: EF Core, Identity persistence, migrations. API: authentication, authorization, validation, endpoint mapping, dependency injection. UI consumes /api; it never enforces ownership as a substitute for backend checks.
Existing Class1 files are placeholders. Replace them as each layer gains actual responsibilities; avoid empty service registration methods.

## Verification to add with implementation
Use an isolated PostgreSQL database in API integration tests (for example, a disposable Testcontainers instance), with the same migrations as the application. Verify 401 without a session, valid/invalid CSRF, successful sign-in/sign-out, rejected malformed logs, paging/filtering, and user A never seeing user B's records. Health is not a database readiness check.

Reference: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-8.0
