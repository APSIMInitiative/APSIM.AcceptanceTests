using AcceptanceTestsWebAPI.Data;

namespace AcceptanceTestsWebAPI.Requests;

/// <summary>Contains a placeholder handler for testing pull request requests.</summary>
public class PullRequest
{
    public static IResult Test(AcceptanceTestsDbContext db)
    {
        return Results.Ok(new { status = "ok" });
    }
}