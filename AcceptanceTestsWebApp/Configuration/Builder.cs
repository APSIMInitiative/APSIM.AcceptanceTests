using Microsoft.AspNetCore.HttpOverrides;

namespace AcceptanceTestsWebApp.Configuration;

public class Builder
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
        options.IdleTimeout = TimeSpan.FromSeconds(60);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss UTC ";
            options.UseUtcTimestamp = true;
            options.SingleLine = true;
        });

        return builder;
    }
}
