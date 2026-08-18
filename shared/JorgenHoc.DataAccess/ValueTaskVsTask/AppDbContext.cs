using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.ValueTaskVsTask;

/// <summary>
/// Context for the ValueTask vs Task article. Named <c>AppDbContext</c> to match the
/// article text; the namespace keeps it distinct from the other articles' contexts.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
}
