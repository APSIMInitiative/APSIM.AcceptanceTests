namespace AcceptanceTestsWebAPI.Data;

/// <summary>Tracks the database download stage for an acceptance-test run.</summary>
public enum DatabaseFetchStatus
{
    /// <summary>Waiting for all tasks to report completion.</summary>
    WaitingForJobs,
    /// <summary>Ready for a worker, or waiting for the next retry.</summary>
    Pending,
    /// <summary>A worker holds a lease to fetch the databases.</summary>
    Fetching,
    /// <summary>All discovered databases were downloaded, including an empty listing.</summary>
    Completed,
    /// <summary>The run timed out before all tasks completed; no fetch was queued.</summary>
    TimedOut
}
