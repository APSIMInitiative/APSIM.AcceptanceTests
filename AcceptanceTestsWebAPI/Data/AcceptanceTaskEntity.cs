namespace AcceptanceTestsWebAPI.Data;

/// <summary>
/// Tracks completion of a task path within a job so repeated notifications are idempotent.
/// </summary>
public class AcceptanceTaskEntity
{
    /// <summary>ID of the job containing this task.</summary>
    public Guid RunId { get; set; }
    /// <summary>Task path supplied when the job started; unique within the job.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Whether this task has reported completion.</summary>
    public bool Completed { get; set; }
    /// <summary>The job associated with this task.</summary>
    public AcceptanceJobEntity Run { get; set; } = null!;
}
