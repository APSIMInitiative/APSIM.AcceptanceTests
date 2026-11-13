# AcceptanceTestsWebApp

ASP.NET Core Razor Components web application for the APSIM acceptance-test
infrastructure. The current app contains a home page, an error page and a
not-found page. Its navigation links to `/admin`, but an admin page is not
implemented. Registration and download workflows are not currently implemented.

## Run locally

With the .NET 10 SDK installed, run from the repository root:

```powershell
dotnet watch run --project AcceptanceTestsWebApp\AcceptanceTestsWebApp.csproj
```

Open the URL printed at startup. In VS Code, the
**Just Watch AcceptanceTestsWebApp** task also runs the application with watch.

## Pages

| Route | Component |
| --- | --- |
| `/` | `Home.razor` |
| `/Error` | `Error.razor` |
| `/not-found` | `NotFound.razor` |

The router also uses the not-found component for unmatched routes.

## Docker

Build from the repository root and map a host port to container port 80:

```powershell
docker build -f AcceptanceTestsWebApp\Dockerfile -t apsim-acceptance-webapp .
docker run -p 8090:80 apsim-acceptance-webapp
```
