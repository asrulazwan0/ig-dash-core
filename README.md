# IGDash Core
.NET 8 API for a dashboard with individual accounts and private logs.

## Current state
The API supports registration, cookie sessions, CSRF protection, and private log creation, editing, deletion, tags, search/date/level filters, pagination, activity summaries, and CSV export. PostgreSQL migrations are applied explicitly. GET /api/health reports process health.

## Local development
Install the .NET 8 SDK, then from this repository:
```sh
dotnet restore IGDash.Core.sln
# Replace the password and port with your local PostgreSQL settings.
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Host=localhost;Port=5432;Database=igdash;Username=igdash;Password=<local-password>' --project src/IGDash.Core.Api
dotnet tool restore
dotnet ef database update --project src/IGDash.Core.Infrastructure --startup-project src/IGDash.Core.Api -- --environment Development
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
Clone ig-dash-core and ig-dash-ui as siblings. Install Docker Compose 2.20.3 or newer (Compose include support). Configure .env as above, then from this repo:
```sh
docker compose -f compose.workspace.yaml up --build -d --wait postgres
docker compose -f compose.workspace.yaml --profile tools run --build --rm migrate
docker compose -f compose.workspace.yaml up --build -d --wait postgres api ui
```
Open http://localhost:5173. API: http://localhost:5193. Sources are mounted for hot reload; database and cookie protection keys persist in named volumes. The tracked workspace file is the shared entry point; no parent configuration is required.
```sh
docker compose -f compose.workspace.yaml --profile tools run --build --rm browser-tests
docker compose -f compose.workspace.yaml logs -f api ui
docker compose -f compose.workspace.yaml stop api ui
```
The last command frees ports for native apps while keeping PostgreSQL running. Browser checks create test accounts and logs; use a disposable stack for test runs when preserving development data matters. Do not remove persistent volumes to switch workflows.

Sessions expire after eight hours. Sign-out revokes all sessions for that account. Production requires HTTPS, a same-origin API proxy, restricted database credentials, and deployment-specific configuration; these files provide development containers.

## Demo data
Apply migrations first. Seeding is explicit and available only in Development; normal startup never seeds or migrates the database.

For Docker, add `IGDASH_DEMO_PASSWORD=<your-strong-demo-password>` to your ignored `.env`, then run:
```sh
docker compose -f compose.workspace.yaml --profile tools run --build --rm seed
```
For native development, use the same configured database as the API:
```sh
dotnet user-secrets set 'Seed:Password' '<your-strong-demo-password>' --project src/IGDash.Core.Api
dotnet run --project src/IGDash.Core.Api --launch-profile http -- --seed-demo
```
Alternatively set `Seed__Password` in the process environment. The password requires at least 12 characters, uppercase, lowercase, a number, and a symbol. It is never printed by the seeder.

Sign in as `demo.one@example.test` or `demo.two@example.test` using your configured password. Each has 84 private entries across four weeks, varied levels and tags, and long/Unicode/multiline examples. Reruns add missing demo records without replacing existing entries or changing account passwords. Deleted demo records return when explicitly reseeding; existing timestamps stay unchanged. No reset or deletion of other data is performed. A conflicting pre-existing demo email stops the entire transaction.
