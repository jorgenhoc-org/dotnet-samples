// One minimal ASP.NET Core app, deployable to every platform in the hosting articles
// with the config files under deploy/. Deliberately tiny: the point of the sample is
// the deployment surface, not the application.
//
// https://www.jorgenhoc.org/en/blog/best-dotnet-hosting
// https://www.jorgenhoc.org/en/blog/cheapest-dotnet-hosting

var builder = WebApplication.CreateBuilder(args);

// Railway, Render, and Heroku-style platforms inject the listen port as PORT at
// RUNTIME. A Dockerfile `ENV ASPNETCORE_URLS=http://+:${PORT:-8080}` can NOT pick that
// up — Docker resolves ${...} when the image is BUILT, so the port is baked in and the
// platform's injected value is ignored. Reading PORT here is the fix; smoke-test.sh
// asserts it works.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "JorgenHoc hosting sample",
    runtime = Environment.Version.ToString(),
    // Every platform stamps its own env vars — handy to confirm where you landed.
    platform = DetectPlatform(),
    listeningOn = string.Join(", ", app.Urls),
}));

// The health endpoint every deploy config under deploy/ points at.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

static string DetectPlatform() =>
    Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null ? "Azure App Service"
    : Environment.GetEnvironmentVariable("CONTAINER_APP_NAME") is not null ? "Azure Container Apps"
    : Environment.GetEnvironmentVariable("FLY_REGION") is not null ? "Fly.io"
    : Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT") is not null ? "Railway"
    : Environment.GetEnvironmentVariable("RENDER") is not null ? "Render"
    : Environment.GetEnvironmentVariable("APP_PLATFORM_COMPONENT_TYPE") is not null ? "DigitalOcean App Platform"
    : "local / VM";
