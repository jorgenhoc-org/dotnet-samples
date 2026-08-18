// Minimal ASP.NET Core app for the Azure App Service article. Deliberately tiny: the point
// of the sample is the deployment, not the application. Each field in the response proves
// one claim the article makes about how App Service configures your app.
//
// https://www.jorgenhoc.org/en/blog/azure-app-service-dotnet

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", (IConfiguration config) => Results.Ok(new
{
    service = "JorgenHoc App Service sample",
    runtime = Environment.Version.ToString(),

    // Set by the ASPNETCORE_ENVIRONMENT app setting — "Production" on Azure unless you
    // override it, "Development" when run locally via launchSettings.json.
    environment = app.Environment.EnvironmentName,

    // "from appsettings.json" locally; after deploy.sh sets the Sample__Message app
    // setting, the deployed app shows that value instead — proving App Settings become
    // environment variables that override appsettings.json.
    message = config["Sample:Message"],

    // Present only when running on App Service. instance changes when you scale out.
    site = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "(not on App Service)",
    instance = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? "(local)",
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
