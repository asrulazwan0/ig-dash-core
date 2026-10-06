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

## Local PostgreSQL
Docker Compose runs PostgreSQL 18 with persistent storage:
```sh
cp .env.example .env
# Edit .env and set your own local POSTGRES_PASSWORD.
docker compose up -d --wait
docker compose ps
```
The database is `igdash`, the username is `igdash`, and the address is `localhost:5432`. Change POSTGRES_PORT in .env if that port is occupied. The port is bound to loopback only.

The API does not connect to the database yet. When persistence is implemented, set `ConnectionStrings__DefaultConnection` in the API environment or `ConnectionStrings:DefaultConnection` using dotnet user-secrets:
```text
Host=localhost;Port=5432;Database=igdash;Username=igdash;Password=<your-local-password>
```
Compose reads .env for the container; dotnet does not automatically read that file. Match the connection string port and password to your .env values.

Stop the database with `docker compose stop`; restart with `docker compose up -d --wait`. Data stays in the named volume. PostgreSQL initialization settings apply only to a new volume; changing .env does not change the password in an existing database. This development database user is an administrator; production should use a separate restricted application role.

## Checks
```sh
dotnet build IGDash.Core.sln --configuration Release
```
There are no tests yet. Add API integration tests alongside authentication and persistence.

## Documentation
- [MVP scope](docs/mvp.md)
- [Architecture, authentication, and proposed API](docs/api-design.md)

Keep secrets in dotnet user-secrets or environment variables. PostgreSQL integration and EF Core migrations will arrive with authentication. GitHub Projects will track implementation once this foundation is reviewed.
