using AcceptanceTestsWebAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceTestsWebAPI.Requests;

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

        PullRequestEntity pr = new PullRequestEntity();
        pr.PullRequest = pullRequest;
        pr.Commit = commit;
        pr.Author = author;
        pr.StartTime = DateTime.Now;

        pr.Name = $"{pr.StartTime.ToString("yyyy-MM-dd-hh-mm-ss")}-{author.ToLower()}-{pullRequest.ToLower()}-{commit.ToLower()}";
        pr.EndTime = pr.StartTime;
        pr.NumberOfTasks = 0;
        pr.NumberOfTasksCompleted = 0;
        pr.Status = Shared.Models.PullRequestStatus.Created;

        await db.PullRequests.AddAsync(pr);
        await db.SaveChangesAsync();

        return Results.Ok(new { status = "ok" });
    }

    public record StartJSON(string PullRequest, string Commit, string[] Paths);
    /// <summary>Marks a pull request as running and records the number of paths to process.</summary>
    /// <param name="db">Database context used to update the record.</param>
    /// <param name="request">Request containing a pull request number, commit hash, and non-empty list of file paths to process.</param>
    /// <example>
    /// <code>
    /// {
    ///   "pullRequest": "123",
    ///   "commit": "a1b2c3d",
    ///   "paths": ["Tests/Wheat.apsimx", "Tests/Barley.apsimx"]
    /// }
    /// </code>
    /// </example>
    public async static Task<IResult> Start(AcceptanceTestsDbContext db, [FromBody]StartJSON request)
    {
        string pullRequest = request.PullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.Commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        List<string> paths = new List<string>();
        if (request.Paths != null)
            foreach (string path in request.Paths)
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
        
        if (paths.Count == 0)
            return Results.BadRequest(new { status = "Json fragment missing list of file paths" });

        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        if (pr.Status == Shared.Models.PullRequestStatus.Running)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} is already running" });

        pr.NumberOfTasks = paths.Count();
        pr.NumberOfTasksCompleted = 0;
        pr.Status = Shared.Models.PullRequestStatus.Running;
        await db.SaveChangesAsync();

        return Results.Ok(new { status = "ok" });
    }

    public record JobCompleteJSON(string PullRequest, string Commit, string Path, double Time, string[] Data);
    /// <summary>Records that a job completed and increments the completed-task count.</summary>
    /// <param name="db">Database context used to update the completion count.</param>
    /// <param name="request">Request containing the pull request number and commit hash, completed file path, elapsed time, and non-empty job data.</param>
    /// <example>
    /// <code>
    /// {
    ///   "pullRequest": "123",
    ///   "commit": "a1b2c3d",
    ///   "path": "Tests/Wheat.apsimx",
    ///   "time": 12.5,
    ///   "data": ["passed"]
    /// }
    /// </code>
    /// </example>
    public async static Task<IResult> JobComplete(AcceptanceTestsDbContext db, [FromBody]JobCompleteJSON request)
    // string pullRequest, string commit, string path, string time)
    {
        string pullRequest = request.PullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.Commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        string path = request.Path;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing file path that was run" });

        double time = request.Time;

        List<string> data = new List<string>();
        if (request.Data != null)
            foreach (string d in request.Data)
                if (!string.IsNullOrEmpty(d))
                    data.Add(d);
        if (data.Count == 0)
            return Results.BadRequest(new { status = "Json fragment missing list of file paths" });

        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        await db.SaveChangesAsync();

        //do an atomic update on the value to prvent race conditions
        await db.PullRequests.Where(row => row.PullRequest == pullRequest && row.Commit == commit)
                             .ExecuteUpdateAsync(s => s.SetProperty(b => b.NumberOfTasksCompleted, b => b.NumberOfTasksCompleted + 1));

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
    private async static Task<PullRequestEntity?> GetpullRequestFromDB(AcceptanceTestsDbContext db, string pullRequest, string commit)
    {
        return await db.PullRequests.FirstOrDefaultAsync(row => row.PullRequest == pullRequest && row.Commit == commit);
    }
}