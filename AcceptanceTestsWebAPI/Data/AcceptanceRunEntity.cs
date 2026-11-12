namespace AcceptanceTestsWebAPI.Data;

/// <summary>Tracks the database download stage for an acceptance-test run.</summary>
public enum DatabaseFetchStatus
{
    /// <summary>Waiting for all jobs to report completion.</summary>
    WaitingForJobs,
    /// <summary>Ready for a worker, or waiting for the next retry.</summary>
    Pending,
    /// <summary>A worker holds a lease to fetch the databases.</summary>
    Fetching,
    /// <summary>All discovered databases were downloaded, including an empty listing.</summary>
    Completed,
    /// <summary>The run timed out before all jobs completed; no fetch was queued.</summary>
    TimedOut
}

/// <summary>
/// Represents one execution of a pull request and its durable database-fetch state.
/// A separate record is retained for each container so reruns remain distinguishable.
/// </summary>
public class AcceptanceRunEntity
{
    /// <summary>Unique run identifier, also used as the local download directory name.</summary>
    public Guid Id { get; set; }
    /// <summary>Pull request that owns this run.</summary>
    public Guid PullRequestId { get; set; }
    /// <summary>Azure Blob Storage container holding this run's outputs.</summary>
    public string ContainerName { get; set; } = string.Empty;
    /// <summary>Current state of the database download.</summary>
    public DatabaseFetchStatus FetchStatus { get; set; }
    /// <summary>Identifies the worker currently claiming this run, if any.</summary>
    public Guid? LeaseId { get; set; }
    /// <summary>UTC expiry after which an abandoned fetch may be reclaimed.</summary>
    public DateTime? LeaseExpiresUtc { get; set; }
    /// <summary>Earliest UTC time at which a failed fetch may be retried.</summary>
    public DateTime? NextAttemptUtc { get; set; }
    /// <summary>Number of times a worker has claimed the fetch.</summary>
    public int FetchAttempts { get; set; }
    /// <summary>Number of databases downloaded by the successful fetch.</summary>
    public int DownloadedFiles { get; set; }
    /// <summary>Latest fetch error or timeout message, when present.</summary>
    public string? FetchError { get; set; }
    /// <summary>The pull request associated with this run.</summary>
    public PullRequestEntity PullRequest { get; set; } = null!;
}

/// <summary>
/// Tracks completion of a path within a run so repeated job notifications are idempotent.
/// </summary>
public class AcceptanceJobEntity
{
    /// <summary>Run containing this job.</summary>
    public Guid RunId { get; set; }
    /// <summary>Job path supplied when the run started; unique within the run.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Whether this job has reported completion.</summary>
    public bool Completed { get; set; }
    /// <summary>The run associated with this job.</summary>
    public AcceptanceRunEntity Run { get; set; } = null!;
}
