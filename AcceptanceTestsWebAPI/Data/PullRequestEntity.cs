using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace AcceptanceTestsWebAPI.Data;

/// <summary>Stores the current execution status and job counts for a pull request and commit.</summary>
public class PullRequestEntity
{    
    /// <summary>Unique identifier for this pull request record.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique display name generated when the record is opened.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Pull request number.</summary>
    [Required]
    public string PullRequest { get; set; } = string.Empty;

    /// <summary>Commit hash identifying the revision under test.</summary>
    [Required]
    public string Commit { get; set; } = string.Empty;

    /// <summary>Author supplied when the record was opened.</summary>
    [Required]
    public string Author { get; set; } = string.Empty;

    /// <summary>Number of jobs expected in the current run.</summary>
    [Required]
    public int NumberOfTasks { get; set; } = 0;

    /// <summary>Number of distinct jobs that have reported completion in the current run.</summary>
    [Required]
    public int NumberOfTasksCompleted { get; set; } = 0;

    /// <summary>The current run, if created with the run-aware API.</summary>
    public Guid? CurrentRunId { get; set; }

    /// <summary>Time the current run was started, or the record was opened before its first run.</summary>
    [Required]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>Time the current run closed or timed out; initially set to its start time.</summary>
    [Required]
    public DateTime EndTime { get; set; } = DateTime.UtcNow;

    /// <summary>Current execution status of the pull request.</summary>
    [Required]
    public PullRequestStatus Status { get; set; } = PullRequestStatus.Created;
}