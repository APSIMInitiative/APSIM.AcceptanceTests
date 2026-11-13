using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Shared.Enums;

namespace AcceptanceTestsWebAPI.Data;

/// <summary>Stores one acceptance-test attempt for a pull request and commit.</summary>
public class AcceptanceJobEntity
{
    /// <summary>Unique identifier for this attempt and its local download directory.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name generated when the pull request record is opened and shared by its attempts.</summary>
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

    /// <summary>Number of task paths expected in this job.</summary>
    public int NumberOfTasks { get; set; } = 0;

    /// <summary>Number of distinct task paths that have reported completion in this job.</summary>
    public int NumberOfTasksCompleted { get; set; } = 0;

    /// <summary>Time this run started, or the record was opened before its first run.</summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>Time this run closed or timed out; initially set to its start time.</summary>
    public DateTime EndTime { get; set; } = DateTime.UtcNow;

    /// <summary>Execution status of this attempt.</summary>
    public PullRequestStatus Status { get; set; } = PullRequestStatus.Created;

    /// <summary>Whether this is the current attempt for the pull request and commit.</summary>
    [JsonIgnore]
    public bool IsCurrent { get; set; } = true;

    /// <summary>Azure container holding this attempt's outputs, once started.</summary>
    [JsonIgnore]
    public string? ContainerName { get; set; }

    /// <summary>Current state of the database download.</summary>
    [JsonIgnore]
    public DatabaseFetchStatus FetchStatus { get; set; }

    /// <summary>Worker currently claiming the fetch, if any.</summary>
    [JsonIgnore]
    public Guid? LeaseId { get; set; }

    /// <summary>UTC expiry after which an abandoned fetch may be reclaimed.</summary>
    [JsonIgnore]
    public DateTime? LeaseExpiresUtc { get; set; }

    /// <summary>Earliest UTC time at which a failed fetch may be retried.</summary>
    [JsonIgnore]
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>Number of times a worker has claimed the fetch.</summary>
    [JsonIgnore]
    public int FetchAttempts { get; set; }

    /// <summary>Number of databases downloaded by the successful fetch.</summary>
    [JsonIgnore]
    public int DownloadedFiles { get; set; }

    /// <summary>Latest fetch error or timeout message, when present.</summary>
    [JsonIgnore]
    public string? FetchError { get; set; }
}