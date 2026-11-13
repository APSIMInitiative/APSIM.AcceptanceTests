using AcceptanceTestsWebApp.Components;

namespace AcceptanceTestsWebApp.Configuration;

/// <summary>Builds the web application and configures its HTTP and Razor component pipeline.</summary>
public class Application
{
    public static WebApplication CreateApplication(WebApplicationBuilder builder)
    {
        WebApplication app = builder.Build();

        app.UseForwardedHeaders();
        app.UseSession();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }
}