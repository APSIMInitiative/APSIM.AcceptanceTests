using AcceptanceTestsWebApp.Configuration;

dotenv.net.DotEnv.Load();
WebApplicationBuilder builder = Builder.CreateBuilder(args);
WebApplication app = Application.CreateApplication(builder);

app.Run();