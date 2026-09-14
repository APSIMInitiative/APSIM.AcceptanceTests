using System.ComponentModel.DataAnnotations;
using Shared.Models;

namespace AcceptanceTestsWebAPI.Data;

public class PullRequestEntity
{    
    /// <summary>
    /// The unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public string Commit { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public int NumberOfTasks { get; set; } = 0;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public int NumberOfTasksCompleted { get; set; } = 0;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public DateTime EndTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 
    /// </summary>
    [Required]
    public PullRequestStatus Status { get; set; } = PullRequestStatus.Created;
}