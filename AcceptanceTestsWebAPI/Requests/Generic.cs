namespace AcceptanceTestsWebAPI.Requests;

/// <summary>Provides the API's basic status and health responses.</summary>
public class Generic
{
    public static IResult Default()
    {
        return Results.Ok(new { status = "ok" });
    }

    public static IResult Health()
    {
        return Results.Ok(new { status = "ok" });
    }
}