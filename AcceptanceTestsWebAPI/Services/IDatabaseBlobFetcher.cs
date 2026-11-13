namespace AcceptanceTestsWebAPI.Services;

/// <summary>Downloads a run's databases and returns the number of files fetched.</summary>
public interface IDatabaseBlobFetcher
{
    Task<int> FetchAsync(Guid runId, string containerName, CancellationToken cancellationToken);
}
