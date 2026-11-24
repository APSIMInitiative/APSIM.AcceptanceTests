using System.Text.RegularExpressions;
using AcceptanceTestsWebAPI.Data;
using AcceptanceTestsWebAPI.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AcceptanceTestsWebAPI.Requests;

/// <summary>Handles pull request lifecycle, job completion and database-fetch status requests.</summary>
public class PullRequestEndpoints
{
    /// <summary>Creates a pull request record for the specified commit.</summary>
    /// <param name="db">Database context used to create the record.</param>
    /// <param name="pullRequest">Pull request number, such as <c>123</c>.</param>
    /// <param name="commit">Commit hash identifying the revision, such as <c>a1b2c3d</c>.</param>
    /// <param name="author">Author's name, such as <c>octocat</c>.</param>
    /// <example>
    /// <code>Open(db, "123", "a1b2c3d", "octocat")</code>
    /// </example>
    public async static Task<IResult> Open(AcceptanceTestsDbContext db, string pullRequest, string commit, string author)
    {
        if (await GetCurrentPullRequestFromDBWithoutTracking(db, pullRequest, commit) != null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} already exists" });

        DateTime now = DateTime.Now;
        AcceptanceJobEntity pr = new()
        {
            Id = Guid.NewGuid(),
            PullRequestNumber = pullRequest,
            Commit = commit,
            Author = author,
            StartTime = now,
            NumberOfTasks = 0,
            NumberOfTasksCompleted = 0,
            Status = PullRequestStatus.Created,
            EndTime = now
        };

        await db.PullRequests.AddAsync(pr);
        await db.SaveChangesAsync();

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Identifies a job's task paths and the UTC timestamp used to create its name.</summary>
    public sealed record StartJSON(string PullRequest, string Commit, string[] Paths, string Datetime);

    /// <summary>Starts an acceptance job for a pull request and records its task paths.</summary>
    /// <param name="db">Database context used to update the record.</param>
    /// <param name="request">Request containing the pull request, commit, distinct job paths and UTC start timestamp.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <example>
    /// <code>
    /// {
    ///   "pullRequest": "123",
    ///   "commit": "a1b2c3d",
    ///   "paths": ["Tests/Wheat.apsimx"],
    ///   "dateTime": "2026-10-15T04:03:22"
    /// }
    /// </code>
    /// </example>
    public async static Task<IResult> Start(AcceptanceTestsDbContext db, [FromBody]StartJSON request, CancellationToken cancellationToken)
    {
        List<string> errors = PullRequestUtility.ValidateStartRequest(request, out DateTime parsedDateTime);
        if (errors.Count > 0)
            return Results.BadRequest(new { status = errors });

        string pullRequestNumber = request.PullRequest;
        string commit = request.Commit;
        string jobName = PullRequestUtility.CreateName(pullRequestNumber, parsedDateTime);

        AcceptanceJobEntity? pr = await GetCurrentPullRequestFromDBWithoutTracking(
            db, pullRequestNumber, commit, cancellationToken);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequestNumber} and commit hash {commit} was not found in the database" });

        if (pr.Status == PullRequestStatus.Running)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequestNumber} and commit hash {commit} is already running" });

        if (await db.PullRequests.AnyAsync(run => run.Name == jobName, cancellationToken))
            return Results.Conflict(new
            {
                status = "The job name has already been used"
            });

        DateTime startTime = DateTime.Now;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Guid runId = string.IsNullOrEmpty(pr.ContainerName) ? pr.Id : Guid.NewGuid();
        var current = db.CurrentPullRequests.Where(row => row.Id == pr.Id &&
            row.Status != PullRequestStatus.Running);
        int updated;
        try
        {
            if (string.IsNullOrEmpty(pr.ContainerName))
            {
                updated = await current.ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.NumberOfTasks, request.Paths.Length)
                    .SetProperty(row => row.NumberOfTasksCompleted, 0)
                    .SetProperty(row => row.Name, jobName)
                    .SetProperty(row => row.StartTime, startTime)
                    .SetProperty(row => row.EndTime, startTime)
                    .SetProperty(row => row.Status, PullRequestStatus.Running), cancellationToken);
            }
            else
            {
                updated = await current.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.EndTime, startTime),
                    cancellationToken);
                if (updated == 1)
                {
                    db.PullRequests.Add(new AcceptanceJobEntity
                    {
                        Id = runId, Name = jobName, PullRequestNumber = pr.PullRequestNumber,
                        Commit = pr.Commit, Author = pr.Author,
                        NumberOfTasks = request.Paths.Length, NumberOfTasksCompleted = 0,
                        StartTime = startTime, EndTime = startTime, Status = PullRequestStatus.Running
                    });
                }
            }
            if (updated == 0)
                return Results.Conflict(new { status = "The pull request is already running" });

            db.Tasks.AddRange(request.Paths.Select(path => new AcceptanceTaskEntity { RunId = runId, Path = path }));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (SqliteException exception) when (IsJobNameUniqueConstraintViolation(exception))
        {
            return Results.Conflict(new { status = "The job name has already been used" });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException sqliteException &&
            IsJobNameUniqueConstraintViolation(sqliteException))
        {
            return Results.Conflict(new { status = "The job name has already been used" });
        }
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Reports a completed task after its output has been uploaded to Azure storage.</summary>
    public sealed record JobCompleteJSON(string PullRequest, string Commit, string Path, double Time, string[] Data, string ContainerName);
    /// <summary>Records that a task completed and increments the completed-task count once.</summary>
    /// <param name="db">Database context used to update the completion count.</param>
    /// <param name="request">Request containing the run container, completed file path, elapsed time, and job data.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <example>
    /// <code>
    /// {
    ///   "pullRequest": "123",
    ///   "commit": "a1b2c3d",
    ///   "path": "Tests/Wheat.apsimx",
    ///   "time": 12.5,
    ///   "data": ["passed"],
    ///   "containerName": "2026-11-12-acceptance-tests-pr-123-storage"
    /// }
    /// </code>
    /// </example>
    public async static Task<IResult> JobComplete(AcceptanceTestsDbContext db, [FromBody]JobCompleteJSON request, CancellationToken cancellationToken)
    {
        string pullRequest = request.PullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.Commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        string path = request.Path;
        if (string.IsNullOrWhiteSpace(path))
            return Results.BadRequest(new { status = "Json fragment missing file path that was run" });

        if (string.IsNullOrWhiteSpace(request.ContainerName))
            return Results.BadRequest(new { status = "Json fragment missing storage container name" });

        List<string> data = new List<string>();
        if (request.Data != null)
            foreach (string d in request.Data)
                if (!string.IsNullOrEmpty(d))
                    data.Add(d);
        if (data.Count == 0)
            return Results.BadRequest(new { status = "Json fragment missing list of file paths" });

        string jobName = request.ContainerName.EndsWith("-storage", StringComparison.Ordinal)
            ? request.ContainerName[..^"-storage".Length]
            : string.Empty;
        AcceptanceJobEntity? pr = string.IsNullOrEmpty(jobName)
            ? null
            : await GetPullRequestByJobNameFromDBWithoutTracking(
                db, jobName, cancellationToken);
        if (pr == null)
        {
            if (await GetCurrentPullRequestFromDBWithoutTracking(db, pullRequest, commit, cancellationToken) == null)
            {
                string status = $"Pull Request with number {pullRequest} and commit hash {commit} " +
                    "was not found in the database";
                return Results.BadRequest(new { status });
            }
            return Results.Conflict(new { status = "The container does not identify the current run" });
        }

        if (pr.PullRequestNumber != pullRequest || pr.Commit != commit ||
            pr.ContainerName != request.ContainerName)
            return Results.Conflict(new { status = "The container does not identify the current run" });

        // Mark the task and increment the count in one transaction. The predicate also
        // rechecks the current run so concurrent or late notifications cannot advance it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int completed = await db.Tasks.Where(job => job.RunId == pr.Id && job.Path == path && !job.Completed &&
                db.CurrentPullRequests.Any(row => row.Id == pr.Id && row.Status == PullRequestStatus.Running))
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Completed, true), cancellationToken);
        if (completed == 1)
        {
            await db.CurrentPullRequests.Where(row => row.Id == pr.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NumberOfTasksCompleted, row => row.NumberOfTasksCompleted + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { status = "ok" });
        }

        // A retry is successful even after the run closes, but not after a different run starts.
        bool alreadyCompleted = await db.Tasks.AsNoTracking().AnyAsync(
            job => job.RunId == pr.Id && job.Path == path && job.Completed &&
                db.CurrentPullRequests.Any(row => row.Id == pr.Id), cancellationToken);
        if (!alreadyCompleted)
            return Results.Conflict(new { status = "Job is not part of the current running pull request" });

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Gets the current job for the specified pull request and commit.</summary>
    /// <param name="db">Database context used to look up the record.</param>
    /// <param name="pullRequest">Pull request number, such as <c>11745</c>.</param>
    /// <param name="commit">Commit hash identifying the revision, such as <c>e027fb703d8a8ee8688bdf80b862a4ebb71dbddf</c>.
    /// This is a full length commit hash.
    /// </param>
    /// <example>
    /// <code>Get(db, "11745", "e027fb703d8a8ee8688bdf80b862a4ebb71dbddf")</code>
    /// </example>
    public async static Task<IResult> Get(AcceptanceTestsDbContext db, string pullRequest, string commit)
    {
        AcceptanceJobEntity? pr = await GetCurrentPullRequestFromDBWithoutTracking(db, pullRequest, commit);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        return Results.Ok(pr); 
    }

    /// <summary>Fetch status for a particular pull request run.</summary>
    public sealed record DatabaseFetchResponse(Guid RunId, string ContainerName, string Status, int Attempts, int DownloadedFiles, string? Error);

    /// <summary>Gets the database fetch status for a run.</summary>
    public async static Task<IResult> GetDatabaseFetch(
        AcceptanceTestsDbContext db,
        string pullRequest,
        string commit,
        string containerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerName) ||
            !containerName.EndsWith("-storage", StringComparison.Ordinal))
            return Results.NotFound(new { status = "Run not found" });
        string jobName = containerName[..^"-storage".Length];
        if (string.IsNullOrEmpty(jobName))
            return Results.NotFound(new { status = "Run not found" });
        var run = await db.PullRequests.AsNoTracking()
            .Where(row => row.Name == jobName)
            .Select(row => new
            {
                row.Id,
                row.PullRequestNumber,
                row.Commit,
                row.FetchStatus,
                row.FetchAttempts,
                row.DownloadedFiles,
                row.FetchError
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (run == null || run.PullRequestNumber != pullRequest || run.Commit != commit)
            return Results.NotFound(new { status = "Run not found" });
        return Results.Ok(new DatabaseFetchResponse(run.Id, containerName, run.FetchStatus.ToString(), run.FetchAttempts, run.DownloadedFiles, run.FetchError));
    }

    /// <summary>Lists the current job for each pull request and commit.</summary>
    /// <param name="db">Database context containing the records.</param>
    /// <example>
    /// <code>List(db)</code>
    /// </example>
    public async static Task<IResult> List(AcceptanceTestsDbContext db)
    {
        return Results.Ok(db.CurrentPullRequests);
    }

    /// <summary>Deletes all jobs for the specified pull request and commit when the token is valid.</summary>
    /// <param name="db">Database context used to remove the record.</param>
    /// <param name="token">Authorization token required by this endpoint.</param>
    /// <param name="pullRequest">Pull request number of the record to delete, such as <c>123</c>.</param>
    /// <param name="commit">Commit hash of the record to delete, such as <c>a1b2c3d</c>.</param>
    /// <example>
    /// <code>Delete(db, token, "123", "a1b2c3d")</code>
    /// </example>
    public async static Task<IResult> Delete(AcceptanceTestsDbContext db, string token, string pullRequest, string commit)
    {
        if (token == "12345678")
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            AcceptanceJobEntity? pr = await GetCurrentPullRequestFromDBWithoutTracking(db, pullRequest, commit);
            if (pr == null)
                return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });
            
            await db.PullRequests.Where(row => row.PullRequestNumber == pullRequest && row.Commit == commit).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { status = "ok" });
        }
        else
        {
            return Results.BadRequest(new { status = $"" });
        }
    }

    /// <summary>Deletes all job attempts when the token is valid.</summary>
    /// <param name="db">Database context containing the records to remove.</param>
    /// <param name="token">Authorization token required by this endpoint.</param>
    /// <example>
    /// <code>DeleteAll(db, token)</code>
    /// </example>
    public async static Task<IResult> DeleteAll(AcceptanceTestsDbContext db, string token)
    {
        if (token == "12345678")
        {
            await db.PullRequests.ExecuteDeleteAsync();
            return Results.Ok(new { status = "ok" });
        }
        else
        {
            return Results.BadRequest(new { status = $"" });
        }
    }

    /// <summary>Finds a specific job by its globally unique name.</summary>
    /// <param name="db">Database context used to query the record.</param>
    /// <param name="jobName">Name identifying the run.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <example>
    /// <code>
    /// GetPullRequestByJobNameFromDBWithoutTracking(db, "job-name", cancellationToken)
    /// </code>
    /// </example>
    private async static Task<AcceptanceJobEntity?> GetPullRequestByJobNameFromDBWithoutTracking(
        AcceptanceTestsDbContext db, string jobName, CancellationToken cancellationToken)
    {
        return await db.PullRequests.AsNoTracking().FirstOrDefaultAsync(
            row => row.Name == jobName, cancellationToken);
    }

    private static bool IsJobNameUniqueConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode == 2067 &&
            exception.Message.Contains("PullRequests.Name", StringComparison.Ordinal);
    }

    /// <summary>Finds the current job by pull request number and commit hash.</summary>
    /// <param name="db">Database context used to query the record.</param>
    /// <param name="pullRequest">Pull request number to find, such as <c>123</c>.</param>
    /// <param name="commit">Commit hash identifying the revision.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    private async static Task<AcceptanceJobEntity?> GetCurrentPullRequestFromDBWithoutTracking(
        AcceptanceTestsDbContext db, string pullRequest, string commit, CancellationToken cancellationToken = default)
    {
        return await db.CurrentPullRequests.AsNoTracking().FirstOrDefaultAsync(row =>
            row.PullRequestNumber == pullRequest &&
            row.Commit == commit, cancellationToken);
    }

}