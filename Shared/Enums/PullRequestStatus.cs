namespace Shared.Enums;

/// <summary>Describes the execution lifecycle of a pull request's current acceptance-test run.</summary>
public enum PullRequestStatus
{
    Created,
    Running,
    Closed,
    Timeout
}
