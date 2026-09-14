using System.ComponentModel;
using System.Net.Mail;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AcceptanceTestsWebAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceTestsWebAPI.Configuration;

public class Application
{
    public static WebApplication CreateApplication(WebApplicationBuilder builder)
    {
        WebApplication app = builder.Build();

        if (!app.Environment.IsEnvironment("Testing"))
        {
            using var scope = app.Services.CreateScope();
            AcceptanceTestsDbContext db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
            db.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "APSIM AcceptanceTests API v1");
                options.DocumentTitle = "APSIM AcceptanceTests API Documentation";
            });
        }

        app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    private static string ResolveTemplateLogoUrl(string? configuredLogoUrl, string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredLogoUrl))
        {
            return configuredLogoUrl;
        }

        return "https://www.apsim.info/wp-content/uploads/2026/05/APSIM_transparent-154x100-1.png";
    }
}