using BlazoredGoogleCaptcha.Services;
using dotenv.net;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using AcceptanceTestsWebApp.Components;

DotEnv.Load();
/*
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WebApiUtility>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<DownloadAccessState>();
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<CircuitHandler, UserCircuitHandler>();
builder.Services.AddHttpClient<APSIMBuildsAPIUtility>();
builder.Services.AddSingleton<CaptchaService>();
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

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSession();
app.Use(async (context, next) =>
{
    var userContext =
        context.RequestServices.GetRequiredService<UserContext>();

    var ip =
        context.Connection.RemoteIpAddress?
            .MapToIPv4()
            .ToString();

    userContext.IPAddress = ip;
    await next();
});

// Force service construction on startup so WebApiUtility configuration is initialized.
_ = app.Services.GetRequiredService<WebApiUtility>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
*/