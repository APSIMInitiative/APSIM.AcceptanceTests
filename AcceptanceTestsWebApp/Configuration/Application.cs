using AcceptanceTestsWebApp.Components;

namespace AcceptanceTestsWebApp.Configuration;

public class Application
{
    public static WebApplication CreateApplication(WebApplicationBuilder builder)
    {
        WebApplication app = builder.Build();

        app.UseForwardedHeaders();
        app.UseSession();

        // Force service construction on startup so WebApiUtility configuration is initialized.
        //_ = app.Services.GetRequiredService<WebApiUtility>();

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