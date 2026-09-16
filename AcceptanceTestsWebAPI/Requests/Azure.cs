using AcceptanceTestsWebAPI.Data;

namespace AcceptanceTestsWebAPI.Requests;

public class PullRequest
{
    public static IResult Test(AcceptanceTestsDbContext db)
    {
        return Results.Ok(new { status = "ok" });
    }
}