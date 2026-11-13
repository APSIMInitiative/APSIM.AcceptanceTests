using AcceptanceTestsWebAPI.Data;
using AcceptanceTestsWebAPI.Requests;
using AcceptanceTestsWebAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;

namespace Tests.AcceptanceTestsWebAPI;

/// <summary>Exercises run completion, idempotency, fetch queuing and worker recovery.</summary>
public sealed class RunWorkflowTests
{
    [Fact]
    public async Task CompletionIsIdempotentAndOnlyQueuesAfterEveryDistinctJob()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one", "two"]);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.CompleteAsync("run-one", "one")));
        await fixture.ManageAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var pr = await db.PullRequests.SingleAsync();
            Assert.Equal(1, pr.NumberOfTasksCompleted);
            Assert.Equal(PullRequestStatus.Running, pr.Status);
            Assert.Equal(DatabaseFetchStatus.WaitingForJobs, (await db.Jobs.SingleAsync()).FetchStatus);
        }

        Assert.Equal(409, await fixture.CompleteAsync("run-one", "not-a-job"));
        Assert.Equal(200, await fixture.CompleteAsync("run-one", "two"));
        await fixture.ManageAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            Assert.Equal(PullRequestStatus.Closed, (await db.PullRequests.SingleAsync()).Status);
            Assert.Equal(DatabaseFetchStatus.Pending, (await db.Jobs.SingleAsync()).FetchStatus);
        }
        Assert.Equal(200, await fixture.CompleteAsync("run-one", "two"));
    }

    [Fact]
    public async Task DistinctConcurrentCompletionsAreAllCounted()
    {
        await using var fixture = await Fixture.CreateAsync();
        string[] paths = Enumerable.Range(0, 20).Select(i => $"job-{i}").ToArray();
        await fixture.StartAsync("run-one", paths);
        int?[] responses = await Task.WhenAll(paths.Select(path => fixture.CompleteAsync("run-one", path)));
        Assert.All(responses, status => Assert.Equal(200, status));
        await Task.WhenAll(fixture.ManageAsync(), fixture.ManageAsync());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        Assert.Equal(20, (await db.PullRequests.SingleAsync()).NumberOfTasksCompleted);
        Assert.Equal(DatabaseFetchStatus.Pending, (await db.Jobs.SingleAsync()).FetchStatus);
    }

    [Fact]
    public async Task RerunRejectsOldNotificationsAndContainerReuse()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();
        using (var previousScope = fixture.Services.CreateScope())
        {
            var previousDb = previousScope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            (await previousDb.PullRequests.SingleAsync()).StartTime = DateTime.Now.AddHours(-2);
            await previousDb.SaveChangesAsync();
        }
        await fixture.StartAsync("run-two", ["one"]);
        await fixture.ManageAsync();
        Assert.Equal(409, await fixture.CompleteAsync("run-one", "one"));
        Assert.Equal(200, await fixture.CompleteAsync("run-two", "one"));
        await fixture.ManageAsync();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        IResult result = await PullRequestEndpoints.Start(db, new("123", "abc", ["one"], "run-one"), CancellationToken.None);
        Assert.Equal(409, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(2, await db.Jobs.CountAsync());
    }

    [Fact]
    public async Task TimedOutRunDoesNotQueueAFetch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            (await db.PullRequests.SingleAsync()).StartTime = DateTime.Now.AddHours(-2);
            await db.SaveChangesAsync();
        }
        await fixture.ManageAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            Assert.Equal(PullRequestStatus.Timeout, (await db.PullRequests.SingleAsync()).Status);
            Assert.Equal(DatabaseFetchStatus.TimedOut, (await db.Jobs.SingleAsync()).FetchStatus);
        }
        Assert.Equal(409, await fixture.CompleteAsync("run-one", "one"));
    }

    [Fact]
    public async Task StartRejectsDuplicatePaths()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        IResult result = await PullRequestEndpoints.Start(db, new("123", "abc", ["one", "one"], "run-one"), CancellationToken.None);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(await db.Jobs.ToListAsync());
    }

    [Fact]
    public async Task FailureIsRecordedAndExpiredLeaseIsRecovered()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();

        var failing = new FakeFetcher((_, _, _) => throw new IOException("test failure"));
        await fixture.Worker(failing).ProcessNextAsync(CancellationToken.None);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var run = await db.Jobs.SingleAsync();
            Assert.Equal(DatabaseFetchStatus.Pending, run.FetchStatus);
            Assert.NotNull(run.FetchError);
            Assert.NotNull(run.NextAttemptUtc);
            run.FetchStatus = DatabaseFetchStatus.Fetching;
            run.LeaseId = Guid.NewGuid();
            run.LeaseExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await fixture.Worker(new FakeFetcher((_, _, _) => Task.FromResult(0))).ProcessNextAsync(CancellationToken.None);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var run = await db.Jobs.SingleAsync();
            Assert.Equal(DatabaseFetchStatus.Completed, run.FetchStatus);
            Assert.Equal(0, run.DownloadedFiles);
            Assert.Null(run.FetchError);
            Assert.Equal(2, run.FetchAttempts);
        }
    }

    [Fact]
    public async Task WorkerWaitsForRetryAndActiveLeaseButClaimsDueRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            (await db.Jobs.SingleAsync()).NextAttemptUtc = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }

        int calls = 0;
        var fetcher = new FakeFetcher((_, _, _) =>
        {
            calls++;
            return Task.FromResult(0);
        });
        var worker = fixture.Worker(fetcher);
        await worker.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(0, calls);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var run = await db.Jobs.SingleAsync();
            run.FetchStatus = DatabaseFetchStatus.Fetching;
            run.LeaseId = Guid.NewGuid();
            run.LeaseExpiresUtc = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        await worker.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(0, calls);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var run = await db.Jobs.SingleAsync();
            run.FetchStatus = DatabaseFetchStatus.Pending;
            run.NextAttemptUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await worker.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentWorkersOnlyClaimTheRunOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var fetcher = new FakeFetcher(async (_, _, ct) =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        });
        Task first = fixture.Worker(fetcher).ProcessNextAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await fixture.Worker(fetcher).ProcessNextAsync(CancellationToken.None);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
    }

    private sealed class FakeFetcher(Func<Guid, string, CancellationToken, Task<int>> fetch) : IDatabaseBlobFetcher
    {
        public Task<int> FetchAsync(Guid runId, string containerName, CancellationToken cancellationToken) =>
            fetch(runId, containerName, cancellationToken);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ava-tests-" + Guid.NewGuid().ToString("N"));
        public ServiceProvider Services { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.directory);
            var services = new ServiceCollection();
            services.AddDbContext<AcceptanceTestsDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(fixture.directory, "test.sqlite")}"));
            fixture.Services = services.BuildServiceProvider();
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            await db.Database.MigrateAsync();
            db.PullRequests.Add(new PullRequestEntity
            {
                Id = Guid.NewGuid(),
                Name = "test",
                PullRequest = "123",
                Commit = "abc",
                Author = "test",
                Status = PullRequestStatus.Created
            });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task StartAsync(string container, string[] paths)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            IResult result = await PullRequestEndpoints.Start(db, new("123", "abc", paths, container), CancellationToken.None);
            Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        }

        public async Task<int?> CompleteAsync(string container, string path)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            IResult result = await PullRequestEndpoints.JobComplete(db, new("123", "abc", path, 1, ["passed"], container), CancellationToken.None);
            return ((IStatusCodeHttpResult)result).StatusCode;
        }

        public Task ManageAsync() => new DatabaseManager(Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DatabaseManager>.Instance).DoWorkAsync(CancellationToken.None);

        public DatabaseFetchWorker Worker(IDatabaseBlobFetcher fetcher) => new(Services.GetRequiredService<IServiceScopeFactory>(),
            fetcher, NullLogger<DatabaseFetchWorker>.Instance);

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
