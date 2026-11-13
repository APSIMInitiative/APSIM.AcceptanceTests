using System.Text;
using AcceptanceTestsWebAPI.Services;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Tests.AcceptanceTestsWebAPI;

/// <summary>Exercises blob selection, local path handling and download failure recovery.</summary>
public sealed class DatabaseBlobFetcherTests
{
    [Fact]
    public async Task DownloadsNestedDatabasesAndIgnoresOtherBlobs()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ava-blobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var container = new FakeContainer(new Dictionary<string, string>
            {
                ["databases/a/tests/first.db"] = "first",
                ["databases/b/first.DB"] = "second",
                ["databases/a/ignore.txt"] = "ignored",
                ["elsewhere/ignore.db"] = "ignored"
            });
            var fetcher = CreateFetcher(directory, container);
            Guid run = Guid.NewGuid();

            Assert.Equal(2, await fetcher.FetchAsync(run, "test-container", CancellationToken.None));
            Assert.Equal("databases/", container.Prefix);
            Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(directory, run.ToString("D"), "a", "tests", "first.db")));
            Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(directory, run.ToString("D"), "b", "first.DB")));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, run.ToString("D")), "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedDownloadDoesNotLeavePartialDatabaseAndCanBeRetried()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ava-blobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var container = new FakeContainer(new() { ["databases/a/test.db"] = "content" }) { FailRead = true };
            var fetcher = CreateFetcher(directory, container);
            Guid run = Guid.NewGuid();
            await Assert.ThrowsAsync<IOException>(() => fetcher.FetchAsync(run, "test-container", CancellationToken.None));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory, run.ToString("D")), "*", SearchOption.AllDirectories));
            container.FailRead = false;
            Assert.Equal(1, await fetcher.FetchAsync(run, "test-container", CancellationToken.None));
            Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(directory, run.ToString("D"), "a", "test.db")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsBlobPathsThatEscapeTheRunDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ava-blobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fetcher = CreateFetcher(directory, new FakeContainer(new() { ["databases/../escape.db"] = "bad" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.FetchAsync(Guid.NewGuid(), "test-container", CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(directory, "escape.db")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DatabaseBlobFetcher CreateFetcher(string directory, BlobContainerClient client) =>
        new(Options.Create(new DatabaseFetchOptions { Directory = directory }), new TestEnvironment { ContentRootPath = directory },
            new FakeProvider(client));

    private sealed class FakeProvider(BlobContainerClient client) : IBlobContainerProvider
    {
        public BlobContainerClient GetContainer(string containerName) => client;
    }

    private sealed class FakeContainer(Dictionary<string, string> blobs) : BlobContainerClient
    {
        public string? Prefix { get; private set; }
        public bool FailRead { get; set; }

        public override AsyncPageable<BlobItem> GetBlobsAsync(BlobTraits traits, BlobStates states, string prefix, CancellationToken cancellationToken = default)
        {
            Prefix = prefix;
            var items = blobs.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => BlobsModelFactory.BlobItem(name: name,
                    properties: CreateBlobProperties()))
                .ToArray();
            return AsyncPageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(items, null, new FakeResponse())]);
        }

        public override BlobClient GetBlobClient(string blobName) => new FakeBlob(blobs[blobName], FailRead);

        private static BlobItemProperties CreateBlobProperties()
        {
            var factory = typeof(BlobsModelFactory).GetMethods()
                .Where(method => method.Name == nameof(BlobsModelFactory.BlobItemProperties))
                .MinBy(method => method.GetParameters().Length)!;
            var arguments = factory.GetParameters()
                .Select(parameter => parameter.Name == "eTag" ? new ETag("\"test\"") :
                    parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
                .ToArray();
            return (BlobItemProperties)factory.Invoke(null, arguments)!;
        }
    }

    private sealed class FakeBlob(string content, bool failRead) : BlobClient
    {
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(BlobDownloadOptions options, CancellationToken cancellationToken = default)
        {
            Assert.Equal(new ETag("\"test\""), options.Conditions.IfMatch);
            Stream stream = failRead ? new FailingStream() : new MemoryStream(Encoding.UTF8.GetBytes(content));
            return Task.FromResult(Response.FromValue(BlobsModelFactory.BlobDownloadStreamingResult(stream, null), (Response)new FakeResponse()));
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Simulated download failure"));
    }

    private sealed class FakeResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = string.Empty;
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = string.Empty; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
