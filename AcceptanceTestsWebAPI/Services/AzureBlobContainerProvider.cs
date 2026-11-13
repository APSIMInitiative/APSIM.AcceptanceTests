using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace AcceptanceTestsWebAPI.Services;

/// <summary>Creates container clients using the configured account and Azure default credentials.</summary>
public sealed class AzureBlobContainerProvider(IOptions<DatabaseFetchOptions> options) : IBlobContainerProvider
{
    public BlobContainerClient GetContainer(string containerName)
    {
        if (!Uri.TryCreate(options.Value.StorageAccountUri, UriKind.Absolute, out Uri? accountUri) ||
            accountUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("DatabaseFetch:StorageAccountUri must be an absolute HTTPS storage account URI.");
        return new BlobServiceClient(accountUri, new DefaultAzureCredential()).GetBlobContainerClient(containerName);
    }
}
