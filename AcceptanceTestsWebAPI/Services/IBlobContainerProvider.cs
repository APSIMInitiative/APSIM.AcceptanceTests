using Azure.Storage.Blobs;

namespace AcceptanceTestsWebAPI.Services;

/// <summary>Provides access to a run's Azure Blob Storage container.</summary>
public interface IBlobContainerProvider
{
    BlobContainerClient GetContainer(string containerName);
}
