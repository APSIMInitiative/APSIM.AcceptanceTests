namespace AcceptanceTestsWebAPI.Data;

/// <summary>
/// Tracks completion of a path within a run so repeated job notifications are idempotent.
/// </summary>
public class AcceptanceTaskEntity
{
    /// <summary>Run containing this job.</summary>
    public Guid RunId { get; set; }
    /// <summary>Job path supplied when the run started; unique within the run.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Whether this job has reported completion.</summary>
    public bool Completed { get; set; }
    /// <summary>The run associated with this job.</summary>
    public AcceptanceJobEntity Run { get; set; } = null!;
}
