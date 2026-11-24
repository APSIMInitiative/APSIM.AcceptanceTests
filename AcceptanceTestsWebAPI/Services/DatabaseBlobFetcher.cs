using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceTestsWebAPI.Services;

/// <summary>
/// Downloads database blobs sequentially, preserving their paths within a run-specific directory.
/// </summary>
public sealed class DatabaseBlobFetcher(
    IOptions<DatabaseFetchOptions> options,
    IWebHostEnvironment environment,
    IBlobContainerProvider containers) : IDatabaseBlobFetcher
{
    public async Task<int> FetchAsync(Guid runId, string containerName, CancellationToken cancellationToken)
    {
        DatabaseFetchOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Directory))
            throw new InvalidOperationException("DatabaseFetch:Directory must be configured on persistent storage.");

        string root = Path.GetFullPath(settings.Directory, environment.ContentRootPath);
        string runDirectory = Path.Combine(root, runId.ToString("D"));
        System.IO.Directory.CreateDirectory(runDirectory);
        BlobContainerClient client = containers.GetContainer(containerName);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        int downloaded = 0;

        await foreach (BlobItem blob in client.GetBlobsAsync(BlobTraits.None, BlobStates.None, "databases/", cancellationToken))
        {
            if (!blob.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                continue;

            string relativeName = blob.Name["databases/".Length..];
            string[] segments = relativeName.Split('/');
            // Blob names are untrusted paths: reject traversal and names unsafe for this filesystem.
            if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".." ||
                    segment.Contains('\\') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    (OperatingSystem.IsWindows() && (segment.EndsWith(' ') || segment.EndsWith('.')))))
                throw new InvalidOperationException($"Blob has an unsafe local path: {blob.Name}");

            string destination = Path.Combine([runDirectory, .. segments]);
            if (!paths.Add(destination))
                throw new InvalidOperationException($"Multiple blobs resolve to the same local path: {blob.Name}");

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                // Match the listed version and publish only a completely downloaded file.
                var conditions = new BlobRequestConditions { IfMatch = blob.Properties.ETag };
                Response<BlobDownloadStreamingResult> response = await client.GetBlobClient(blob.Name)
                    .DownloadStreamingAsync(new BlobDownloadOptions { Conditions = conditions }, cancellationToken);
                await using (Stream source = response.Value.Content)
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await source.CopyToAsync(target, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
                downloaded++;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        return downloaded;
    }
}
