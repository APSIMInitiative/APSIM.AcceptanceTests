# AcceptanceTestsWebAPI

ASP.NET Core API for tracking acceptance-test job attempts, task completion,
and downloading SQLite database outputs from Azure Blob Storage.

## Run locally

With the .NET 10 SDK installed, run from the repository root:

```powershell
dotnet run --project AcceptanceTestsWebAPI\AcceptanceTestsWebAPI.csproj
```

Use the URL printed at startup. Swagger UI is available at `/swagger` in the
Development environment only. The API loads environment variables from `.env`
at startup as well as the usual ASP.NET Core configuration sources.

## Security

**The active endpoints are anonymous; authentication and authorization are not
enabled.** The destructive test endpoints use a hard-coded placeholder token,
not real authentication, and use GET requests. Do not expose this API to
untrusted networks without appropriate access controls.

The global rate limiter allows 300 requests per minute per client IP.

## API endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api` | API status |
| GET | `/api/health` | Health response |
| GET | `/api/pr/open` | Create the initial job record; query: `pullRequest`, `commit`, `author` |
| POST | `/api/pr/start` | Start the initial job or a new attempt |
| POST | `/api/pr/jobcomplete` | Record completion of a task path |
| GET | `/api/pr/get` | Get the current job; query: `pullRequest`, `commit` |
| GET | `/api/pr/list` | List current jobs |
| GET | `/api/pr/fetch` | Get an attempt's fetch status; query: `pullRequest`, `commit`, `containerName` |
| GET | `/api/test/delete` | Delete all attempts for a PR and commit; query: `token`, `pullRequest`, `commit` |
| GET | `/api/test/deleteall` | Delete all job records; query: `token` |

Deleting records also deletes their task records; it does not delete downloaded
files or Azure containers.

Open a PR and commit before starting its first job. A start request requires a
non-empty list of distinct paths and a valid Azure container name. A rerun
requires a different container name for that PR and commit:

```http
POST /api/pr/start
Content-Type: application/json

{
  "pullRequest": "123",
  "commit": "a1b2c3d",
  "paths": ["Tests/Wheat.apsimx", "Tests/Barley.apsimx"],
  "containerName": "acceptance-pr-123-attempt-1"
}
```

Once a task's outputs have been uploaded, report its completion:

```http
POST /api/pr/jobcomplete
Content-Type: application/json

{
  "pullRequest": "123",
  "commit": "a1b2c3d",
  "path": "Tests/Wheat.apsimx",
  "time": 12.5,
  "data": ["passed"],
  "containerName": "acceptance-pr-123-attempt-1"
}
```

`data` must contain at least one non-empty item; neither `data` nor `time` is
persisted. Duplicate completion notifications are idempotent while the
attempt remains current. Notifications for a superseded attempt are rejected.

For the state transitions, retries and polling example, see
[Database fetch workflow](../Docs/DATABASE_FETCH_WORKFLOW.md).

## Database downloads

Set `DatabaseFetch:StorageAccountUri` to the HTTPS URI of the Azure storage
account and `DatabaseFetch:Directory` to a directory on persistent storage.
Environment variables `DatabaseFetch__StorageAccountUri` and
`DatabaseFetch__Directory` can override these settings. The configured default
download directory is `FetchedDatabases`, relative to the API content root.
Mount it on persistent storage when running in a container.

The worker uses `DefaultAzureCredential`; its identity needs permission to list
and read blobs. Once all tasks complete, it downloads `.db` blobs from the
`databases/` prefix to `<download directory>/<job ID>/<path under databases/>`.
An empty listing succeeds with zero files. Failed downloads are retried; fetch
status and errors are available from `/api/pr/fetch`.

## SQLite and migrations

`ConnectionStrings:AcceptanceTestsDb` configures the SQLite connection; the
default file is `APSIMAcceptanceTestsSystemWebAPI.db`. Migrations are applied
at startup except in the `Testing` environment.

**The current migration is a fresh baseline, not an upgrade from the previous
schema.** Back up any existing database and use a new empty database or reset it
before starting this version. Resetting removes the previous data.

After future EF Core model changes, create a migration from the API project
directory. The Windows script `update-ef-migration.bat` prompts for a name,
adds the migration **and updates the configured database**; review the target
database before running it.

## Docker

Build from the repository root and map a host port to container port 80:

```powershell
docker build -f AcceptanceTestsWebAPI\Dockerfile -t apsim-acceptance-api .
docker run -p 8080:80 apsim-acceptance-api
```

Provide Azure configuration and persistent storage for the SQLite database and
download directory when deploying.
