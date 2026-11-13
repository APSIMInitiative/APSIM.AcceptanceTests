# Database fetch workflow

This document describes how the API retrieves acceptance-test output databases
from Azure Blob Storage. It is separate from the API's own SQLite database:
SQLite stores pull request, run, job and fetch state; the downloaded `.db`
files are retained as files on disk.

## End-to-end sequence

1. **Register the pull request.** `GET /api/pr/open` creates a pull request
   record for a pull request number, commit and author. The API requires this
   record before a run can start.
2. **Start a run.** `POST /api/pr/start` supplies the pull request, commit,
   exact Azure container name and a non-empty list of distinct job paths.
   `PullRequestEndpoints.Start` validates the request, creates a run and a
   job record for each path, and sets the pull request to `Running`. The
   run starts in `WaitingForJobs`. A container name cannot be reused for a
   run of the same pull request and commit.
3. **Upload and report each job.** The external job runner uploads its output
   to Azure, then calls `POST /api/pr/jobcomplete` with the same pull request,
   commit and container name, plus the job path and result data. Each distinct
   job increments the completed count once. Repeating a completed job's
   notification is safe while that run is still current; unknown paths and
   notifications for a superseded run are rejected. A job may produce no
   database, but must report completion only after all of its uploads finish.
4. **Queue the fetch.** Every 15 seconds, `DatabaseManager` checks running
   pull requests. When all jobs have reported completion, it closes the API's
   pull request record and changes the run to `Pending` in one SQLite
   transaction. This does not close the GitHub pull request. If jobs have not
   completed within 30 minutes of the run's start, the manager marks the
   run `TimedOut` instead and does not queue a fetch.
5. **Claim and download.** `DatabaseFetchWorker` looks for due pending runs
   and abandoned, expired leases. It claims one run, sets it to `Fetching`,
   and calls `DatabaseBlobFetcher` with the run ID and container name. The
   fetcher lists blobs under `databases/` in that container and downloads
   `.db` files, including those in nested directories.
6. **Record the outcome.** On success, the worker changes the run to
   `Completed` and stores the number of files downloaded. An empty listing is
   successful and produces a count of zero. On failure it logs the exception,
   records a generic error, and schedules a retry. Call
   `GET /api/pr/fetch?pullRequest=123&commit=a1b2c3d&containerName=...` to
   inspect the run ID, status, attempts, downloaded count and latest error.

The fetcher lists the container rather than using the job paths to construct
blob names. Outputs must not be modified after a job reports completion.
Examples of the request bodies and deployment settings are in the
[API README](../AcceptanceTestsWebAPI/README.md#fetching-acceptance-test-databases).

## Responsibilities

| Class or component | Role |
| --- | --- |
| [`Program`](../AcceptanceTestsWebAPI/Program.cs) | Maps the pull request lifecycle and fetch-status HTTP endpoints. |
| [`PullRequestEndpoints`](../AcceptanceTestsWebAPI/Requests/PullRequestEndpoints.cs) | Validates requests, creates runs and jobs, records idempotent job completion, and exposes fetch status. The job-completion update and count increment are transactional. |
| [`PullRequestEntity`](../AcceptanceTestsWebAPI/Data/PullRequestEntity.cs) | Stores the pull request's current run ID, status, start time and job counts. |
| [`AcceptanceRunEntity`, `AcceptanceJobEntity` and `DatabaseFetchStatus`](../AcceptanceTestsWebAPI/Data/AcceptanceRunEntity.cs) | Store each run's container and durable fetch state, and track each job path's completion. A separate run record distinguishes reruns. |
| [`AcceptanceTestsDbContext`](../AcceptanceTestsWebAPI/Data/AcceptanceTestsDbContext.cs) | Persists pull requests, runs and jobs in SQLite; enforces uniqueness of a container per pull request record and a job path per run. |
| [`DatabaseManager`](../AcceptanceTestsWebAPI/Data/DatabaseManager.cs) | Polls running pull requests, closes completed runs and queues downloads, or marks incomplete runs as timed out. |
| [`DatabaseFetchWorker`](../AcceptanceTestsWebAPI/Services/DatabaseFetchWorker.cs) | Polls the durable queue, claims and renews leases, calls the fetcher, and records success or schedules retries. |
| [`IDatabaseBlobFetcher` and `DatabaseBlobFetcher`](../AcceptanceTestsWebAPI/Services/DatabaseBlobFetcher.cs) | Define and implement downloading the database files for one run. |
| [`IBlobContainerProvider` and `AzureBlobContainerProvider`](../AcceptanceTestsWebAPI/Services/DatabaseBlobFetcher.cs) | Obtain a container client for the configured Azure account using `DefaultAzureCredential`. |
| [`DatabaseFetchOptions`](../AcceptanceTestsWebAPI/Services/DatabaseBlobFetcher.cs) | Holds the storage account URI and local download directory. |
| [`Builder`](../AcceptanceTestsWebAPI/Configuration/Builder.cs) and [`Application`](../AcceptanceTestsWebAPI/Configuration/Application.cs) | Register the services and SQLite context, and apply EF Core migrations at startup except in the `Testing` environment. |

## State, leases and retries

```text
WaitingForJobs --all jobs complete--> Pending --claimed--> Fetching --success--> Completed
       |                                ^                    |
       |                                +------failure-------+
       +--run timeout--> TimedOut
```

`Pending` includes runs waiting for their retry time. The worker processes
at most one run per polling iteration, with a 15-second timer. Claims are
conditional updates so concurrent workers cannot both claim the same eligible
run. A claim increments the attempt count and receives a unique lease valid
for five minutes; the worker renews it every minute during the download.
Only a worker with the matching, unexpired lease can record success.

If a download fails, the worker returns the run to `Pending` with exponential
delays of 1, 2, 4, and so on, up to 64 minutes. If a worker stops or loses
its lease, a worker can reclaim the run after the lease expires. A retry
lists the container and downloads the files again. Failures are recorded in
server logs; the status endpoint returns a generic error rather than the
exception details.

## Blob selection and local files

- Configure `DatabaseFetch:StorageAccountUri` as the HTTPS storage account
  URI and `DatabaseFetch:Directory` as the download root. Environment variables
  `DatabaseFetch__StorageAccountUri` and `DatabaseFetch__Directory` override
  these settings. Relative directories resolve against the API content root.
- `AzureBlobContainerProvider` uses `DefaultAzureCredential`. The identity
  needs permission to list and read blobs in the run's containers, such as
  the Storage Blob Data Reader role. Keep credentials out of configuration files.
- The fetcher lists the `databases/` prefix and selects `.db` extensions
  case-insensitively. For example, `databases/wheat/results.db` is saved as
  `<download-root>/<run-id>/wheat/results.db`.
- Files are downloaded sequentially. Unsafe path segments and local-path
  collisions are rejected. The listed blob's ETag is used as a download
  condition, so a changed blob causes a failure rather than silently fetching a
  different version.
- Each download goes to a unique temporary `.part` file before being moved
  over its destination. Temporary files are cleaned up on completion or
  failure; previously completed files may remain if a later download fails.
  Existing destination files are overwritten on retry. Files are retained
  for later processing; this workflow does not calculate statistics or
  remove files no longer present in the container.

Use persistent storage for both the SQLite database and the download directory.
Run one API instance unless both are shared with reliable locking.
