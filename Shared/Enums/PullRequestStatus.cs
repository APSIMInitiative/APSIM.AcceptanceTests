namespace Shared.Enums;

/// <summary>Describes the execution lifecycle of an acceptance-test job.</summary>
public enum PullRequestStatus
{
    Created,
    Running,
    Closed,
    Timeout
}
