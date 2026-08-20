using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace JorgenHoc.MigrationsWalkthrough;

/// <summary>
/// Lets `dotnet ef` build the context at design time by reading the same appsettings.json
/// the app uses. Without this, the tools try to construct AppDbContext through the app's
/// host — this keeps migration commands working from a plain `dotnet ef` invocation.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(config.GetConnectionString("LocalDbConnection"))
            .Options;

        return new AppDbContext(options);
    }
}
