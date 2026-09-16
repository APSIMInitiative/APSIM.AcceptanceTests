using AcceptanceTestsWebAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceTestsWebAPI.Requests;

public class PullRequestEndpoints
{
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

    public record StartJSON(string pullRequest, string commit, string[] paths);
    public async static Task<IResult> Start(AcceptanceTestsDbContext db, [FromBody]StartJSON request)
    {
        string pullRequest = request.pullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        List<string> paths = new List<string>();
        if (request.paths != null)
            foreach (string path in request.paths)
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

    public record JobCompleteJSON(string pullRequest, string commit, string path, double time, string[] data);
    public async static Task<IResult> JobComplete(AcceptanceTestsDbContext db, [FromBody]JobCompleteJSON request)
    // string pullRequest, string commit, string path, string time)
    {
        string pullRequest = request.pullRequest;
        if (string.IsNullOrEmpty(pullRequest))
            return Results.BadRequest(new { status = "Json fragment missing pullRequest number" });

        string commit = request.commit;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing commit hash" });

        string path = request.path;
        if (string.IsNullOrEmpty(commit))
            return Results.BadRequest(new { status = "Json fragment missing file path that was run" });

        double time = request.time;

        List<string> data = new List<string>();
        if (request.data != null)
            foreach (string d in request.data)
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

    public async static Task<IResult> Get(AcceptanceTestsDbContext db, string pullRequest, string commit)
    {
        PullRequestEntity? pr = await GetpullRequestFromDB(db, pullRequest, commit);
        if (pr == null)
            return Results.BadRequest(new { status = $"Pull Request with number {pullRequest} and commit hash {commit} was not found in the database" });

        return Results.Ok(pr); 
    }

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

    private async static Task<PullRequestEntity?> GetpullRequestFromDB(AcceptanceTestsDbContext db, string pullRequest, string commit)
    {
        return await db.PullRequests.FirstOrDefaultAsync(row => row.PullRequest == pullRequest && row.Commit == commit);
    }
}