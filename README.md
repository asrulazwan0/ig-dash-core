# IGDash Core
.NET 8 API for a dashboard with individual accounts and private logs.

## Current state
The Clean Architecture solution and GET /api/health are runnable. Authentication, database persistence, and log endpoints are not implemented yet.

## Local development
Install the .NET 8 SDK, then from this repository:
```sh
dotnet restore IGDash.Core.sln
dotnet run --project src/IGDash.Core.Api --launch-profile http
```
API: http://localhost:5193/api/health. Swagger: http://localhost:5193/swagger.
Start the sibling ig-dash-ui app in another terminal; its Vite server proxies /api here. Local HTTP is for loopback development; production must use HTTPS.

## Checks
```sh
dotnet build IGDash.Core.sln --configuration Release
```
There are no tests yet. Add API integration tests alongside authentication and persistence.

## Documentation
- [MVP scope](docs/mvp.md)
- [Architecture, authentication, and proposed API](docs/api-design.md)

Keep secrets in dotnet user-secrets or environment variables. SQLite storage and migrations will arrive with authentication. GitHub Projects will track implementation once this foundation is reviewed.
