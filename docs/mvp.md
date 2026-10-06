# IGDash MVP

## First release
Individual accounts with private logs. A user signs in, submits a log, and views only their own logs. Shared workspaces, invitations, roles, external connectors, charts, and VibeStar are deferred.

## Acceptance criteria
- Registration, sign-in, sign-out, and current-session lookup work.
- A signed-in user can create and list their logs, newest first, with pagination.
- Log fields: UUID id, message (1–2000 characters), level (info/warning/error), UTC occurredAt and createdAt. Owner ID is assigned from the authenticated session, never a request field.
- Missing authentication returns 401. Every read/write filters by authenticated owner. Cross-user ID access returns 404 when detail endpoints are added.
- Validation failures use Problem Details; the UI supports loading, empty, and error states.
- Integration tests cover anonymous requests and two users with separate data.

## Current batch
Runnable React/TypeScript app, API health endpoint, local instructions, CI, and authentication/API design. Account endpoints and database persistence are planned, not implemented.

## Implementation sequence
1. Identity database, migrations, cookie sessions, CSRF protection, and authentication integration tests.
2. Owned log entity, persistence, validated create/list API, pagination, and isolation tests.
3. Sign-in and registration screens, log form/table, filtering, and end-to-end verification.
4. Production deployment configuration, email confirmation/reset delivery, backups, and operational checks.
