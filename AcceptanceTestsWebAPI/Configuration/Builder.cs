using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using AcceptanceTestsWebAPI.Data;

namespace AcceptanceTestsWebAPI.Configuration;

public class Builder
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Configure logging with UTC timestamps.
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss UTC ";
            options.UseUtcTimestamp = true;
            options.SingleLine = true;
        });

        // Add environment variables to configuration (mapped from Env__Var__Name to Section:Key).
        builder.Configuration
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Password"] = Environment.GetEnvironmentVariable("Auth__Password"),
                ["Auth:Username"] = Environment.GetEnvironmentVariable("Auth__Username"),
                ["Jwt:Issuer"] = Environment.GetEnvironmentVariable("Jwt__Issuer"),
                ["Jwt:Audience"] = Environment.GetEnvironmentVariable("Jwt__Audience"),
                ["Jwt:SigningKey"] = Environment.GetEnvironmentVariable("Jwt__SigningKey"),
                ["Jwt:TokenExpiryMinutes"] = Environment.GetEnvironmentVariable("Jwt__TokenExpiryMinutes"),
                ["Verification:BaseUrl"] = Environment.GetEnvironmentVariable("Verification__BaseUrl"),
                ["Download:BaseUrl"] = Environment.GetEnvironmentVariable("Download__BaseUrl"),
                ["Download:TokenLifetimeHours"] = Environment.GetEnvironmentVariable("Download__TokenLifetimeHours"),
                ["Branding:LogoUrl"] = Environment.GetEnvironmentVariable("Branding__LogoUrl")
            });

/*
        string jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        string jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        string jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        string verificationBaseUrl = builder.Configuration["Verification:BaseUrl"] ?? throw new InvalidOperationException("Verification:BaseUrl is not configured.");
        int jwtExpiryMinutes = int.TryParse(builder.Configuration["Jwt:TokenExpiryMinutes"], out var expiryMinutes) ? expiryMinutes : 60;
        int verificationTokenLifetimeHours = int.TryParse(builder.Configuration["Verification:TokenLifetimeHours"], out var tokenLifetimeHours) ? tokenLifetimeHours : 24;
        int downloadTokenLifetimeHours = int.TryParse(builder.Configuration["Download:TokenLifetimeHours"], out var configuredDownloadTokenLifetimeHours)
            ? configuredDownloadTokenLifetimeHours
            : 48;
        if (downloadTokenLifetimeHours <= 0)
        {
            downloadTokenLifetimeHours = 48;
        }*/

        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHostedService<DatabaseManager>();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = "APSIM AcceptanceTests API",
                Description = "API for managing users and organisations in the APSIM AcceptanceTests System."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter a valid JWT bearer token."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", hostDocument: document, externalResource: null)] = new List<string>()
            });
        });

        builder.Services.AddDbContext<AcceptanceTestsDbContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("AcceptanceTestsDb")));
/*
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
*/
        builder.Services.AddAuthorization();
        builder.Services.AddDataProtection();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIpPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("auth-token", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIpPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("public-downloads", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIpPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("authenticated-api", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIpPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 180,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return builder;
    }

    private static string GetClientIpPartitionKey(HttpContext httpContext)
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp;
    }
}
