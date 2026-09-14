namespace AcceptanceTestsWebAPI.Requests;

public class Generic
{
    public static IResult Default()
    {
        return Results.Ok(new
        {
            service = "APSIM AcceptanceTests API",
            status = "ok",
            health = "/health",
            swagger = "/swagger"
        });
    }

    public static IResult Health()
    {
        return Results.Ok(new { status = "ok" });
    }
}