using System.Collections.Concurrent;
using System.Globalization;
using AcceptanceTestsWebAPI.Data;
using AcceptanceTestsWebAPI.Requests;
using AcceptanceTestsWebAPI.Services;
using AcceptanceTestsWebAPI.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;

namespace Tests.AcceptanceTestsWebAPI;

/// <summary>Exercises run completion, idempotency, fetch queuing and worker recovery.</summary>
public sealed class RunWorkflowTests
{
    [Theory]
    [InlineData(3, 5, 0, "2026-03-05-00-07-09-acceptance-tests-pr-123")]
    [InlineData(11, 24, 12, "2026-11-24-12-07-09-acceptance-tests-pr-123")]
    public void CreateNameUsesMonthAnd24HourTime(int month, int day, int hour, string expected)
    {
        var dateTime = new DateTime(2026, month, day, hour, 7, 9);

        Assert.Equal(expected, PullRequestUtility.CreateName("123", dateTime));
    }

    [Fact]
    public async Task MigrationPreservesExistingRunsAndRenamesPullRequestColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>().UseSqlite(connection).Options;
        await using var db = new AcceptanceTestsDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261123040428_DeriveCurrentAttemptFromStartTime");
        Guid unstartedId = Guid.NewGuid();
        Guid startedId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PullRequests" ("Id", "Name", "PullRequest", "Commit", "Author",
                "NumberOfTasks", "NumberOfTasksCompleted", "StartTime", "EndTime", "Status",
                "ContainerName", "FetchStatus", "FetchAttempts", "DownloadedFiles")
            VALUES ({unstartedId}, 'old-name', '123', 'abc', 'author', 0, 0,
                '2026-10-15 04:03:22', '2026-10-15 04:03:22', 0, NULL, 0, 0, 0),
                ({startedId}, 'old-name', '456', 'def', 'author', 1, 0,
                '2026-10-15 04:03:22', '2026-10-15 04:03:22', 1, 'azure-job-storage', 0, 0, 0)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Jobs" ("RunId", "Path", "Completed") VALUES ({startedId}, 'job', 0)""");

        await migrator.MigrateAsync();

        var runs = await db.PullRequests.AsNoTracking().ToDictionaryAsync(run => run.Id);
        Assert.Equal("123", runs[unstartedId].PullRequestNumber);
        Assert.Equal(string.Empty, runs[unstartedId].Name);
        Assert.Equal(string.Empty, runs[unstartedId].ContainerName);
        Assert.Equal("456", runs[startedId].PullRequestNumber);
        Assert.Equal("azure-job", runs[startedId].Name);
        Assert.Equal("azure-job-storage", runs[startedId].ContainerName);
        Assert.Equal("def", runs[startedId].Commit);
        Assert.Equal("job", (await db.Tasks.AsNoTracking().SingleAsync()).Path);

        await migrator.MigrateAsync("20261123054105_RequireAcceptanceJobName");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"PullRequest\" FROM \"PullRequests\" WHERE \"Commit\" = 'def'";
        Assert.Equal("456", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MigrationRejectsLegacyContainerThatCannotBeDerived()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>().UseSqlite(connection).Options;
        await using var db = new AcceptanceTestsDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261123040428_DeriveCurrentAttemptFromStartTime");
        Guid id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PullRequests" ("Id", "Name", "PullRequest", "Commit", "Author",
                "NumberOfTasks", "NumberOfTasksCompleted", "StartTime", "EndTime", "Status",
                "ContainerName", "FetchStatus", "FetchAttempts", "DownloadedFiles")
            VALUES ({id}, 'old-name', '123', 'abc', 'author', 1, 0,
                '2026-10-15 04:03:22', '2026-10-15 04:03:22', 1, 'legacy-container', 0, 0, 0)
            """);

        var error = await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAsync());
        Assert.Contains("ContainerNameMustEndInStorage", error.Message);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"ContainerName\" FROM \"PullRequests\"";
        Assert.Equal("legacy-container", await command.ExecuteScalarAsync());
    }

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
            Assert.Equal(DatabaseFetchStatus.WaitingForJobs, pr.FetchStatus);
        }

        Assert.Equal(409, await fixture.CompleteAsync("run-one", "not-a-job"));
        Assert.Equal(200, await fixture.CompleteAsync("run-one", "two"));
        await fixture.ManageAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            Assert.Equal(PullRequestStatus.Closed, (await db.PullRequests.SingleAsync()).Status);
            Assert.Equal(DatabaseFetchStatus.Pending, (await db.PullRequests.SingleAsync()).FetchStatus);
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
        Assert.Equal(DatabaseFetchStatus.Pending, (await db.PullRequests.SingleAsync()).FetchStatus);
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
        IResult result = await PullRequestEndpoints.Start(
            db, new("123", "abc", ["one"], fixture.GetDateTime("run-one")), CancellationToken.None);
        Assert.Equal(409, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(2, await db.PullRequests.CountAsync(row => row.Name != string.Empty));
    }

    [Fact]
    public async Task RerunKeepsEarlierFetchIndependentWithDistinctJobIds()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();
        Guid oldRunId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var oldRun = await db.PullRequests.SingleAsync();
            oldRunId = oldRun.Id;
        }

        await fixture.StartAsync("run-two", ["one"]);
        var fetcher = new FakeFetcher((runId, container, _) =>
        {
            Assert.Equal(oldRunId, runId);
            Assert.Equal(fixture.GetContainerName("run-one"), container);
            return Task.FromResult(2);
        });
        await fixture.Worker(fetcher).ProcessNextAsync(CancellationToken.None);

        using var resultScope = fixture.Services.CreateScope();
        var resultDb = resultScope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        var attempts = await resultDb.PullRequests.OrderBy(row => row.Name).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(
            new[] { fixture.GetJobName("run-one"), fixture.GetJobName("run-two") },
            attempts.Select(attempt => attempt.Name));
        Assert.All(attempts, attempt => Assert.Equal($"{attempt.Name}-storage", attempt.ContainerName));
        Assert.Equal(oldRunId, attempts[0].Id);
        Assert.NotEqual(attempts[0].Id, attempts[1].Id);
        Assert.True(attempts[1].StartTime > attempts[0].StartTime);
        Assert.Equal(DatabaseFetchStatus.Completed, attempts[0].FetchStatus);
        Assert.Equal(2, attempts[0].DownloadedFiles);
        Assert.Equal(attempts[1].Id, (await resultDb.CurrentPullRequests.SingleAsync()).Id);
        Assert.Equal(PullRequestStatus.Running, attempts[1].Status);
        Assert.Equal(409, await fixture.CompleteAsync("run-one", "one"));
        var get = (IValueHttpResult)await PullRequestEndpoints.Get(resultDb, "123", "abc");
        var current = Assert.IsType<AcceptanceJobEntity>(get.Value);
        Assert.Equal(attempts[1].Id, current.Id);
        string json = System.Text.Json.JsonSerializer.Serialize(current);
        Assert.DoesNotContain("\"AttemptId\"", json);
        Assert.DoesNotContain("\"CurrentRunId\"", json);
        Assert.Contains(current.Id.ToString(), json);
        Assert.DoesNotContain("\"ContainerName\"", json);
        var list = (IValueHttpResult)await PullRequestEndpoints.List(resultDb);
        Assert.Single(await Assert.IsAssignableFrom<IQueryable<AcceptanceJobEntity>>(list.Value).ToListAsync());
        var fetch = (IValueHttpResult)await PullRequestEndpoints.GetDatabaseFetch(
            resultDb, "123", "abc", fixture.GetContainerName("run-one"), CancellationToken.None);
        Assert.Equal(oldRunId, Assert.IsType<PullRequestEndpoints.DatabaseFetchResponse>(fetch.Value).RunId);
        await PullRequestEndpoints.Delete(resultDb, "12345678", "123", "abc");
        Assert.Empty(await resultDb.PullRequests.ToListAsync());
        Assert.Empty(await resultDb.Tasks.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRerunsCreateOnlyOneCurrentAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync("run-one", ["one"]);
        await fixture.CompleteAsync("run-one", "one");
        await fixture.ManageAsync();

        int?[] results = await Task.WhenAll(fixture.StartStatusAsync("run-two", ["one"]),
            fixture.StartStatusAsync("run-three", ["one"]));
        Assert.Single(results, status => status == 200);
        Assert.All(results, status => Assert.Contains(status, new int?[] { 200, 400, 409 }));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        Assert.Equal(2, await db.PullRequests.CountAsync());
        Assert.Single(await db.CurrentPullRequests.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentFirstStartsCreateOnlyOneAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        int?[] results = await Task.WhenAll(fixture.StartStatusAsync("run-one", ["one"]),
            fixture.StartStatusAsync("run-two", ["one"]));
        Assert.Single(results, status => status == 200);
        Assert.All(results, status => Assert.Contains(status, new int?[] { 200, 400, 409 }));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        Assert.Single(await db.PullRequests.ToListAsync());
        Assert.Single(await db.Tasks.ToListAsync());
    }

    [Theory]
    [InlineData("UPDATE \"PullRequests\" SET \"NumberOfTasks\" = NULL")]
    [InlineData("UPDATE \"PullRequests\" SET \"NumberOfTasksCompleted\" = NULL")]
    [InlineData("UPDATE \"PullRequests\" SET \"StartTime\" = NULL")]
    [InlineData("UPDATE \"PullRequests\" SET \"EndTime\" = NULL")]
    [InlineData("UPDATE \"PullRequests\" SET \"Status\" = NULL")]
    public async Task BaselineMigrationRejectsNullExecutionFields(string sql)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
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
            Assert.Equal(DatabaseFetchStatus.TimedOut, (await db.PullRequests.SingleAsync()).FetchStatus);
        }
        Assert.Equal(409, await fixture.CompleteAsync("run-one", "one"));
    }

    [Fact]
    public async Task RunWithoutContainerIsNotQueuedForFetch()
    {
        await using var fixture = await Fixture.CreateAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            var run = await db.PullRequests.SingleAsync();
            run.Status = PullRequestStatus.Running;
            run.NumberOfTasks = 1;
            run.NumberOfTasksCompleted = 1;
            await db.SaveChangesAsync();
        }

        await fixture.ManageAsync();
        using var resultScope = fixture.Services.CreateScope();
        var resultDb = resultScope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        var result = await resultDb.PullRequests.SingleAsync();
        Assert.Equal(PullRequestStatus.Running, result.Status);
        Assert.Equal(DatabaseFetchStatus.WaitingForJobs, result.FetchStatus);
    }

    [Fact]
    public async Task StartRejectsDuplicatePaths()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        IResult result = await PullRequestEndpoints.Start(
            db, new("123", "abc", ["one", "one"], fixture.GetDateTime("run-one")), CancellationToken.None);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.False(await db.PullRequests.AnyAsync(row => row.Name != string.Empty));
    }

    [Theory]
    [InlineData("""{"pullRequest":"","commit":"","paths":null,"datetime":""}""")]
    [InlineData("""{"pullRequest":"","commit":"","paths":[],"datetime":""}""")]
    public async Task StartReportsAllMissingFields(string json)
    {
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new AcceptanceTestsDbContext(options);
        var request = System.Text.Json.JsonSerializer.Deserialize<PullRequestEndpoints.StartJSON>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(request);

        IResult result = await PullRequestEndpoints.Start(db, request, CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        var response = System.Text.Json.JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
        Assert.Equal(
            new[]
            {
                "Json fragment missing pullRequest number",
                "Json fragment missing commit hash",
                "A non-empty list of job paths is required",
                "Json fragment missing dateTime string"
            },
            response.GetProperty("status").EnumerateArray().Select(error => error.GetString()));
    }

    [Fact]
    public async Task StartReportsInvalidAndDuplicatePathsAlongsideInvalidDateTime()
    {
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new AcceptanceTestsDbContext(options);

        IResult result = await PullRequestEndpoints.Start(
            db, new("123", "abc", ["", ""], "not-a-date"), CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        var response = System.Text.Json.JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
        Assert.Equal(
            new[]
            {
                "A non-empty list of job paths is required",
                "Job paths must be unique within a run",
                "dateTime must be a UTC timestamp in yyyy-MM-ddTHH:mm:ss format"
            },
            response.GetProperty("status").EnumerateArray().Select(error => error.GetString()));
    }

    [Theory]
    [InlineData("2026-10-15T04:03:22Z")]
    [InlineData("2026-10-15T04:03:22+00:00")]
    [InlineData("2026-10-15T04:03:22-04:00")]
    [InlineData("2026-10-15T04:03:22.123456")]
    [InlineData("not-a-date")]
    public async Task StartRejectsNonUtcIsoDateTime(string dateTime)
    {
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new AcceptanceTestsDbContext(options);

        IResult result = await PullRequestEndpoints.Start(
            db, new("123", "abc", ["one"], dateTime), CancellationToken.None);

        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task StartAcceptsUtcIsoDateTimeWithoutSuffix()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AcceptanceTestsDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AcceptanceTestsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.PullRequests.Add(new AcceptanceJobEntity
        {
            Id = Guid.NewGuid(),
            PullRequestNumber = "123",
            Commit = "abc",
            Author = "test"
        });
        await db.SaveChangesAsync();

        IResult result = await PullRequestEndpoints.Start(
            db, new("123", "abc", ["one"], "2026-10-15T04:03:22"), CancellationToken.None);

        Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
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
            var run = await db.PullRequests.SingleAsync();
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
            var run = await db.PullRequests.SingleAsync();
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
            (await db.PullRequests.SingleAsync()).NextAttemptUtc = DateTime.UtcNow.AddHours(1);
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
            var run = await db.PullRequests.SingleAsync();
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
            var run = await db.PullRequests.SingleAsync();
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
        private readonly ConcurrentDictionary<string, string> dateTimes = new();
        private int nextDateTime;
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
            db.PullRequests.Add(new AcceptanceJobEntity
            {
                Id = Guid.NewGuid(),
                PullRequestNumber = "123",
                Commit = "abc",
                Author = "test",
                Status = PullRequestStatus.Created
            });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task StartAsync(string container, string[] paths)
        {
            Assert.Equal(200, await StartStatusAsync(container, paths));
        }

        public async Task<int?> StartStatusAsync(string container, string[] paths)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            IResult result = await PullRequestEndpoints.Start(
                db, new("123", "abc", paths, GetDateTime(container)), CancellationToken.None);
            return ((IStatusCodeHttpResult)result).StatusCode;
        }

        public async Task<int?> CompleteAsync(string container, string path)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            IResult result = await PullRequestEndpoints.JobComplete(
                db,
                new("123", "abc", path, 1, ["passed"], GetContainerName(container)),
                CancellationToken.None);
            return ((IStatusCodeHttpResult)result).StatusCode;
        }

        public string GetDateTime(string key) => dateTimes.GetOrAdd(
            key,
            _ => new DateTime(2026, 10, 15, 4, 3, 22, DateTimeKind.Utc)
                .AddSeconds(Interlocked.Increment(ref nextDateTime))
                .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

        public string GetJobName(string key) => PullRequestUtility.CreateName(
            "123",
            DateTime.ParseExact(
                GetDateTime(key),
                "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

        public string GetContainerName(string key) => $"{GetJobName(key)}-storage";

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
