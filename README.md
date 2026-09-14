# Workflow backend

This directory is the backend solution root. It targets .NET 10.

- `services/Workflow.Api`: HTTP API with controllers, models, services, persistence, and messaging folders.
- `services/Notification.Worker`: background notification host with separate persistence and messaging folders.
- `contracts`: shared notification contract, compiled as `Workflow.Contracts` and referenced by both services.
- `tests/Workflow.Tests` and `tests/Notification.Tests`: xUnit projects referencing their respective services; no tests have been added yet.

Build and run from this directory:

```sh
dotnet build WorkflowBackend.sln
dotnet test WorkflowBackend.sln
dotnet run --project services/Workflow.Api --launch-profile http
dotnet run --project services/Notification.Worker
```

The existing sample endpoint remains available at `http://localhost:5159/weatherforecast`.

To run both services in containers:

```sh
docker compose up --build
```

The containerized API also uses port 5159. Stop the local API before starting Compose.

Database contexts are empty EF Core scaffolds. Choose a provider, add entities, register each context with its host, and generate migrations before using persistence. Messaging folders and `EmailService` are placeholders; the worker starts and waits for shutdown until a message consumer is implemented. No database, message broker, or email provider is configured.
