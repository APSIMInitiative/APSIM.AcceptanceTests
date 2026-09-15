namespace AcceptanceTestsWebAPI.Requests;

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