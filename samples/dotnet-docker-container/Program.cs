// Minimal ASP.NET Core app for the container article. Deliberately tiny: the point of the
// sample is the image, not the application.
//
// https://www.jorgenhoc.org/en/blog/dotnet-docker-container

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "JorgenHoc container sample",
    runtime = Environment.Version.ToString(),
    // Confirms the image actually runs as the non-root user the Dockerfile switches to.
    user = Environment.UserName,
}));

// The target of the Dockerfile HEALTHCHECK.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
