using AcceptanceTestsWebAPI.Data;

namespace AcceptanceTestsWebAPI.Requests;

public class Azure
{
    public static IResult Test(AcceptanceTestsDbContext db)
    {
        return Results.Ok(new { status = "ok" });
    }
}