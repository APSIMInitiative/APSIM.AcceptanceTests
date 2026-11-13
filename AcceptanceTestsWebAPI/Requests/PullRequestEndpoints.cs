using System.Text.RegularExpressions;
using AcceptanceTestsWebAPI.Data;                                                                                       
using Microsoft.AspNetCore.Mvc;
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
        if (await GetpullRequestFromDB(db, pullRequest, commit) != null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} already exists" });

        DateTime now = DateTime.Now;
        PullRequestEntity pr = new PullRequestEntity
        {
            PullRequest = pullRequest,
            Commit = commit,
            Author = author,
            StartTime = now,
            NumberOfTasks = 0,
            NumberOfTasksCompleted = 0,
            Status = PullRequestStatus.Created,
            Name = $"{now.ToString("yyyy-MM-dd-hh-mm-ss")}-{author.ToLower()}-{pullRequest.ToLower()}-{commit.ToLower()}",
            EndTime = now
        };

        await db.PullRequests.AddAsync(pr);
        await db.SaveChangesAsync();

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Identifies a run's jobs and its Azure storage container.</summary>
    public sealed record StartJSON(string PullRequest, string Commit, string[] Paths, string ContainerName);
    /// <summary>Marks a pull request as running and records the number of paths to process.</summary>
    /// <param name="db">Database context used to update the record.</param>
    /// <param name="request">Request containing the pull request, commit, distinct job paths and unique storage container.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <example>
    /// <code>
    /// {
    ///   "pullRequest": "123",
    ///   "commit": "a1b2c3d",
    ///   "paths": ["Tests/Wheat.apsimx", "Tests/Barley.apsimx"],
    ///   "containerName": "2026-11-12-acceptance-tests-pr-123-storage"
    /// }
    /// </code>
    /// </example>
    public async static Task<IResult> Start(AcceptanceTestsDbContext db, [FromBody]StartJSON request, CancellationToken cancellationToken)
    {
        string pullRequest = request.PullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.Commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        if (request.Paths == null || request.Paths.Length == 0 ||
            request.Paths.Any(string.IsNullOrWhiteSpace))
            return Results.BadRequest(new { status = "A non-empty list of job paths is required" });
        if (request.Paths.Distinct(StringComparer.Ordinal).Count() != request.Paths.Length)
            return Results.BadRequest(new { status = "Job paths must be unique within a run" });

        // 3-63 lowercase letters or digits, with single hyphens only between them.
        const string azureContainerNamePattern = @"^(?=.{3,63}$)[a-z0-9]+(?:-[a-z0-9]+)*$";
        if (string.IsNullOrWhiteSpace(request.ContainerName) ||
            !Regex.IsMatch(request.ContainerName, azureContainerNamePattern))
            return Results.BadRequest(new { status = "A valid Azure storage container name is required" });

        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit, cancellationToken);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        if (pr.Status == PullRequestStatus.Running)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} is already running" });

        if (await db.Jobs.AnyAsync(run => run.PullRequestId == pr.Id && run.ContainerName == request.ContainerName, cancellationToken))
            return Results.Conflict(new { status = "The container name has already been used for this pull request and commit" });

        Guid runId = Guid.NewGuid();
        DateTime startTime = DateTime.Now;
        // The conditional update prevents concurrent start requests from both creating a run.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int updated = await db.PullRequests.Where(row => row.Id == pr.Id && row.Status != PullRequestStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.NumberOfTasks, request.Paths.Length)
                .SetProperty(row => row.NumberOfTasksCompleted, 0)
                .SetProperty(row => row.CurrentRunId, runId)
                .SetProperty(row => row.StartTime, startTime)
                .SetProperty(row => row.EndTime, startTime)
                .SetProperty(row => row.Status, PullRequestStatus.Running), cancellationToken);
        if (updated == 0)
            return Results.Conflict(new { status = "The pull request is already running" });

        db.Jobs.Add(new AcceptanceJobEntity { Id = runId, PullRequestId = pr.Id, ContainerName = request.ContainerName });
        db.Tasks.AddRange(request.Paths.Select(path => new AcceptanceTaskEntity { RunId = runId, Path = path }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Reports a completed job after its output has been uploaded to Azure storage.</summary>
    public sealed record JobCompleteJSON(string PullRequest, string Commit, string Path, double Time, string[] Data, string ContainerName);
    /// <summary>Records that a job completed and increments the completed-task count.</summary>
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

        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit, cancellationToken);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        AcceptanceJobEntity? run = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(
            row => row.PullRequestId == pr.Id && row.ContainerName == request.ContainerName, cancellationToken);
        if (run == null || pr.CurrentRunId != run.Id)
            return Results.Conflict(new { status = "The container does not identify the current run" });

        // Mark the job and increment the count in one transaction. The predicate also
        // rechecks the current run so concurrent or late notifications cannot advance it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int completed = await db.Tasks.Where(job => job.RunId == run.Id && job.Path == path && !job.Completed &&
                db.PullRequests.Any(row => row.Id == pr.Id && row.CurrentRunId == run.Id && row.Status == PullRequestStatus.Running))
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Completed, true), cancellationToken);
        if (completed == 1)
        {
            await db.PullRequests.Where(row => row.Id == pr.Id && row.CurrentRunId == run.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NumberOfTasksCompleted, row => row.NumberOfTasksCompleted + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { status = "ok" });
        }

        // A retry is successful even after the run closes, but not after a different run starts.
        bool alreadyCompleted = await db.Tasks.AsNoTracking().AnyAsync(
            job => job.RunId == run.Id && job.Path == path && job.Completed &&
                db.PullRequests.Any(row => row.Id == pr.Id && row.CurrentRunId == run.Id), cancellationToken);
        if (!alreadyCompleted)
            return Results.Conflict(new { status = "Job is not part of the current running pull request" });

        return Results.Ok(new { status = "ok" });
    }

    /// <summary>Gets the pull request record for the specified pull request and commit.</summary>
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
        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        return Results.Ok(pr); 
    }

    /// <summary>Fetch status for a particular pull request run.</summary>
    public sealed record DatabaseFetchResponse(Guid RunId, string ContainerName, string Status, int Attempts, int DownloadedFiles, string? Error);

    /// <summary>Gets the database fetch status for a run.</summary>
    public async static Task<IResult> GetDatabaseFetch(AcceptanceTestsDbContext db, string pullRequest, string commit, string containerName, CancellationToken cancellationToken)
    {
        var run = await db.Jobs.AsNoTracking().Where(row => row.PullRequest.PullRequest == pullRequest &&
                row.PullRequest.Commit == commit && row.ContainerName == containerName)
            .Select(row => new { row.Id, row.ContainerName, row.FetchStatus, row.FetchAttempts, row.DownloadedFiles, row.FetchError })
            .FirstOrDefaultAsync(cancellationToken);
        if (run == null)
            return Results.NotFound(new { status = "Run not found" });
        return Results.Ok(new DatabaseFetchResponse(run.Id, run.ContainerName, run.FetchStatus.ToString(), run.FetchAttempts, run.DownloadedFiles, run.FetchError));
    }

    /// <summary>Lists all pull request records.</summary>
    /// <param name="db">Database context containing the records.</param>
    /// <example>
    /// <code>List(db)</code>
    /// </example>
    public async static Task<IResult> List(AcceptanceTestsDbContext db)
    {
        return Results.Ok(db.PullRequests);
    }

    /// <summary>Deletes the specified pull request record when the token is valid.</summary>
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
            PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit);
            if (pr == null)
                return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });
            
            db.PullRequests.Remove(pr);
            await db.SaveChangesAsync();
            return Results.Ok(new { status = "ok" });
        }
        else
        {
            return Results.BadRequest(new { status = $"" });
        }
    }

    /// <summary>Deletes all pull request records when the token is valid.</summary>
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

    /// <summary>Finds a pull request record by pull request number and commit hash.</summary>
    /// <param name="db">Database context used to query the record.</param>
    /// <param name="pullRequest">Pull request number to find, such as <c>123</c>.</param>
    /// <param name="commit">Commit hash to find, such as <c>a1b2c3d</c>.</param>
    /// <example>
    /// <code>GetpullRequestFromDB(db, "123", "a1b2c3d")</code>
    /// </example>
    private async static Task<PullRequestEntity?> GetpullRequestFromDB(AcceptanceTestsDbContext db, string pullRequest, string commit, CancellationToken cancellationToken = default)
    {
        return await db.PullRequests.FirstOrDefaultAsync(row => row.PullRequest == pullRequest && row.Commit == commit, cancellationToken);
    }
}