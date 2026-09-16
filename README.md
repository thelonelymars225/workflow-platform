# Workflow backend

Small .NET 10 / EF Core API for creating and listing workflow definitions. Pair it with the [Angular admin frontend](https://github.com/thelonelymars225/workflow-platform-adminFrontEnd). No worker, broker, Docker, or email service is needed for this slice.

## Prerequisites and local start

- .NET 10 SDK (checked with 10.0.401; runtime/EF Core 10.0.12).
- A running PostgreSQL 16+ development server; create a dedicated database and role with permission to create tables. Npgsql EF provider is pinned to 10.0.1.
- Frontend: Node 24 LTS, npm 11, Angular 21.2 (see its README).

Run from this repository root. First, the API can start **without a database**:

```sh
dotnet restore WorkflowBackend.sln --disable-parallel
dotnet build WorkflowBackend.sln --no-restore /m:1
dotnet test WorkflowBackend.sln --no-restore /m:1
dotnet run --project services/Workflow.Api --launch-profile http
```

Open `http://localhost:5159/health` (or `/api/health`) for liveness, and `http://localhost:5159/openapi/v1.json` for the Development-only OpenAPI document. There is no Swagger UI. Liveness does not promise database readiness. `/health/db` checks connectivity; workflow operations also require the migration below. The existing `/weatherforecast` sample is unchanged.

## PostgreSQL configuration and migration

On a running local PostgreSQL server, connect with your installation's administrator account (adjust host/user if needed):

```sh
psql -h localhost -U postgres -d postgres
```

For a new development database, run these commands once inside `psql`. `\password` prompts interactively, so no password is written in the SQL:

```text
CREATE ROLE workflow_dev LOGIN;
\password workflow_dev
CREATE DATABASE workflow_dev OWNER workflow_dev;
\quit
```

Use the password you just chose in the local configuration below. The database-owning role can apply the initial migration; do not use the administrator account for the API.

Keep credentials out of tracked files. Configure a local user secret (replace all example values):

```sh
dotnet user-secrets set 'ConnectionStrings:WorkflowDatabase' 'Host=localhost;Port=5432;Database=workflow_dev;Username=workflow_dev;Password=REPLACE_ME' --project services/Workflow.Api
dotnet tool restore
dotnet ef database update --project services/Workflow.Api
```

Alternatively set `ConnectionStrings__WorkflowDatabase` in the API process environment. User secrets are loaded in Development; when running EF outside a Development shell, set `ASPNETCORE_ENVIRONMENT=Development` to use them. The initial migration creates `Workflows` with UUID ID, required name (200 characters), optional description (2,000 characters), and UTC created/updated timestamps. The API uses one scoped `WorkflowDbContext`; it never auto-migrates or calls `EnsureCreated`.

Optional seed, only after applying migrations:

```sh
dotnet run --project services/Workflow.Api --launch-profile http -- --Development:SeedData=true
```

The seed is disabled by default and ignored outside Development. Its fixed ID and `ON CONFLICT DO NOTHING` make repeated/concurrent starts idempotent without overwriting existing data. Disable it by omitting the argument. Seed failure stops startup with an actionable diagnostic.

## Endpoints and smoke check

- `GET /api/workflows`: 200 with an array, including `[]` when empty.
- `POST /api/workflows`: JSON `{ "name": "First workflow", "description": null }`; 201 with the saved DTO and a `Location` header.
- `GET /api/workflows/{id}`: 200 with the DTO, or 404 for an unknown UUID.
- Missing/blank/overlong names and overlong descriptions: 400 validation problem. Missing/unreachable database: safe 503 problem; no connection string or stack trace in responses.

After starting PostgreSQL, applying migrations, and starting both apps:

1. Open `http://localhost:4200`, check API connected and the empty list (with seed disabled on a fresh database).
2. Create a named workflow, optionally with a description; confirm one new row in the list and record its ID.
3. Refresh the browser, stop/restart the API, and refresh again: the same ID must remain. Retrieve it directly at `/api/workflows/{id}`; request another valid UUID to check 404.
4. Submit a whitespace name in the UI; directly POST `{ "name": " " }` to check API 400. Check name/description limits too.
5. Stop the API and refresh/submit: visible errors must preserve input. Restart it with PostgreSQL unavailable: liveness stays 200 and workflow requests/readiness return 503.
6. Restart twice with the seed enabled and verify the sample ID `65f64362-d031-4dca-a7af-85991e7a08c0` occurs once.

Unit tests and SQL script generation do **not** verify persistence. Applying the migration, seed repeatability, successful CRUD, and browser create/refresh/API-restart persistence require a real PostgreSQL server. They were not verified in the restricted setup environment, which could not start PostgreSQL as an unprivileged user.

## Code map and troubleshooting

- `services/Workflow.Api/Program.cs`: configuration, scoped context, safe errors, health and Development OpenAPI/seed.
- `Controllers/WorkflowsController.cs` and `Models/WorkflowDtos.cs` (under the API): list/create/get and boundary validation.
- `Data/WorkflowDbContext.cs`, `Data/Migrations`, `Data/DevelopmentSeed.cs`: PostgreSQL schema and opt-in seed.
- `tests/Workflow.Tests`: validation tests; the notification test project has no tests yet.
- `services/Notification.Worker` and `contracts`: existing scaffolding, not involved in workflow CRUD.

503 "not configured": set the connection key above and restart. 503 "unavailable": check PostgreSQL is running, credentials/port/database are correct, and migrations were applied. `/health/db` can succeed before tables exist, so also test `/api/workflows`. HTTP development does not need a trusted HTTPS certificate. If port 5159 is occupied, stop the other process or change the HTTP profile and the frontend's `proxy.conf.json` together. OpenAPI is intentionally unavailable outside Development. Production hosting/authentication and the existing Compose placeholders are outside this local setup.
