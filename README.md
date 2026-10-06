# IGDash Core
.NET 8 API for a dashboard with individual accounts and private logs.

## Current state
The API includes PostgreSQL persistence and an initial Identity account-schema migration. GET /api/health remains a process-health endpoint. Sign-in and log endpoints are not implemented yet; schema changes are applied explicitly.

## Local development
Install the .NET 8 SDK, then from this repository:
```sh
dotnet restore IGDash.Core.sln
# Replace the password and port with your local PostgreSQL settings.
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Host=localhost;Port=5432;Database=igdash;Username=igdash;Password=<local-password>' --project src/IGDash.Core.Api
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

The API registers PostgreSQL persistence. Set `ConnectionStrings__DefaultConnection` in the API environment or `ConnectionStrings:DefaultConnection` using dotnet user-secrets:
```text
Host=localhost;Port=5432;Database=igdash;Username=igdash;Password=<your-local-password>
```
Compose reads .env for the container; dotnet does not automatically read that file. Match the connection string port and password to your .env values.

Stop the database with `docker compose stop`; restart with `docker compose up -d --wait`. Data stays in the named volume. PostgreSQL initialization settings apply only to a new volume; changing .env does not change the password in an existing database. This development database user is an administrator; production should use a separate restricted application role.

## Checks
```sh
dotnet build IGDash.Core.sln --configuration Release
```
Run `dotnet test IGDash.Core.sln --configuration Release` with Docker available. Persistence tests create disposable PostgreSQL containers and do not target your development database. Migration procedures are maintained in the private Project.

Keep secrets in dotnet user-secrets or environment variables.

## Full Docker development
This repo includes a Dockerfile.dev for source-mounted development. With both repos checked out as siblings under `ig/`, the local parent compose.yaml runs the API, frontend, and PostgreSQL together:
```sh
cd ..
docker compose up --build -d --wait
```
Configure ig-dash-core/.env first. The shared setup includes compose.api.yaml, which configures the API using the database service name and the same local password. See ../README.md for hot reload, logs, and switching back to native apps. Parent orchestration files are local and are not tracked in either repo yet.
