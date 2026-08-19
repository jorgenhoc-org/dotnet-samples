using JorgenHoc.DataAccess.EfCoreManyToMany;
using JorgenHoc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // GetConnectionString
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Statement counts for both many-to-many shapes in
// https://www.jorgenhoc.org/en/blog/ef-core-many-to-many
//
//   Posts <-> Tags        implicit (EF Core-managed PostTag junction table)
//   Students <-> Courses  explicit join entity with payload (EnrolledAt, FinalGrade)
//
// Seed the database first — see this folder's README.
//
//   dotnet run                 summary table only
//   dotnet run -- --sql        also print every statement (use this for screenshots)

var printSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);

// EF Core logs through ILoggerFactory, so the host's default console provider prints
// every statement whether or not LogTo is configured. Dropping it means the output below
// is exactly what this program asked for, and statements are not printed twice.
builder.Logging.ClearProviders();

var connectionString = builder.Configuration.GetConnectionString("LocalDbConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'LocalDbConnection' is missing from appsettings.json.");

var counter = new QueryCounter();

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .CountStatements(counter, printSql)
    .EnableSensitiveDataLogging()); // parameter values in the log; never in production

using var host = builder.Build();

await VerifySeedAndResetAsync();

// The article's claim that EF Core creates the junction table by convention, proven by
// reading the columns back from the database rather than asserting it in prose.
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var columns = await db.Database
        .SqlQuery<string>($@"SELECT COLUMN_NAME AS [Value]
                             FROM INFORMATION_SCHEMA.COLUMNS
                             WHERE TABLE_SCHEMA = 'ManyToMany' AND TABLE_NAME = 'PostTag'
                             ORDER BY ORDINAL_POSITION")
        .ToListAsync();

    Console.WriteLine($"Junction table PostTag, no entity class: ({string.Join(", ", columns)})");
}

var report = new StatementReport(
    "EF Core many-to-many — 5 posts, 4 tags, 3 students, 3 courses, SQL Server LocalDB");

// ---------------------------------------------------------------------------
// Querying — implicit junction (Posts <-> Tags)
// ---------------------------------------------------------------------------

await MeasureAsync("Include tags (implicit junction, one query)", async db =>
    await db.Posts.Include(p => p.Tags).OrderBy(p => p.PublishedAt).ToListAsync());

await MeasureAsync("Filtered Include (active tags only)", async db =>
    await db.Posts.Include(p => p.Tags.Where(t => t.IsActive)).ToListAsync());

await MeasureAsync("Where Tags.Any(slug == 'dotnet') + Include", async db =>
{
    var dotnetPosts = await db.Posts
        .Where(p => p.Tags.Any(t => t.Slug == "dotnet"))
        .Include(p => p.Tags)
        .ToListAsync();

    if (dotnetPosts.Count != 2)
        throw new InvalidOperationException($"Expected 2 dotnet posts, got {dotnetPosts.Count}.");
});

// ---------------------------------------------------------------------------
// Querying — explicit join entity with payload (Students <-> Courses)
// ---------------------------------------------------------------------------

await MeasureAsync("Join entity with payload (grades + Include)", async db =>
{
    var topStudents = await db.StudentCourses
        .Where(sc => sc.CourseId == 1 && sc.FinalGrade == Grade.A)
        .Include(sc => sc.Student)
        .Select(sc => sc.Student)
        .ToListAsync();

    if (topStudents.Count != 2)
        throw new InvalidOperationException($"Expected 2 A-grade students, got {topStudents.Count}.");
});

await MeasureAsync("Aggregate: average grade per course", async db =>
    await db.Courses
        .Select(c => new
        {
            c.Title,
            AverageGrade = c.StudentCourses
                .Where(sc => sc.FinalGrade.HasValue)
                .Average(sc => (double?)sc.FinalGrade),
        })
        .ToListAsync());

// ---------------------------------------------------------------------------
// Modifying the implicit collection
// ---------------------------------------------------------------------------

await MeasureAsync("Add tag: Include collection + Find tag + save", async db =>
{
    var post = await db.Posts
        .Include(p => p.Tags)
        .FirstAsync(p => p.Id == 1);                     // statement 1

    var tag = await db.Tags.FindAsync(4);                // statement 2
    post.Tags.Add(tag!);
    await db.SaveChangesAsync();                         // statement 3: INSERT into PostTag
});

await MeasureAsync("Add tag: Attach() stubs, nothing loaded", async db =>
{
    // The stub trick from the article: no reads at all, one INSERT into the junction
    // table. `required` members still demand a value at construction — null! is the
    // honest way to say "never read, only the key matters here".
    var post = new Post { Id = 2, Title = null!, Content = null! };
    var tag = new Tag { Id = 4, Name = null!, Slug = null! };

    db.Attach(post);
    db.Attach(tag);

    post.Tags.Add(tag);
    await db.SaveChangesAsync();                         // 1 statement
});

await MeasureAsync("Remove tag (Include + save)", async db =>
{
    var post = await db.Posts
        .Include(p => p.Tags)
        .FirstAsync(p => p.Id == 1);                     // statement 1

    var tag = post.Tags.First(t => t.Id == 4);
    post.Tags.Remove(tag);
    await db.SaveChangesAsync();                         // statement 2: DELETE from PostTag
});

await MeasureAsync("Bulk: ExecuteDelete on the junction rows", async db =>
{
    // The junction table has no entity class, so it is addressed as a shared-type
    // entity. Removes both of post 4's tags in a single set-based DELETE.
    var deleted = await db.Set<Dictionary<string, object>>("PostTag")
        .Where(pt => (int)pt["PostsId"] == 4)
        .ExecuteDeleteAsync();

    if (deleted != 2)
        throw new InvalidOperationException($"Expected to delete 2 junction rows, got {deleted}.");
});

// ---------------------------------------------------------------------------
// Modifying through the explicit join entity
// ---------------------------------------------------------------------------

await MeasureAsync("Enroll: exists-check + insert join entity", async db =>
{
    var alreadyEnrolled = await db.StudentCourses
        .AnyAsync(sc => sc.StudentId == 3 && sc.CourseId == 3);   // statement 1
    if (alreadyEnrolled)
        throw new InvalidOperationException("Reset failed: Carla is already in Compilers.");

    db.StudentCourses.Add(new StudentCourse
    {
        StudentId = 3,
        CourseId = 3,
        EnrolledAt = DateTime.UtcNow,
    });
    await db.SaveChangesAsync();                                  // statement 2
});

report.Print();

// ---------------------------------------------------------------------------
// The article's warning, tested: what does `post.Tags = newList` actually do?
// ---------------------------------------------------------------------------

Console.WriteLine("Replacing the collection instance (post.Tags = new list):");
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Post 2 currently has tags {dotnet, csharp, legacy}. Replace the loaded List with
    // a brand-new one containing only entity-framework.
    var post = await db.Posts.Include(p => p.Tags).FirstAsync(p => p.Id == 2);
    var efTag = await db.Tags.FindAsync(2);

    post.Tags = [efTag!];
    await db.SaveChangesAsync();

    var slugs = await db.Tags
        .Where(t => t.Posts.Any(p => p.Id == 2))
        .OrderBy(t => t.Slug)
        .Select(t => t.Slug)
        .ToListAsync();

    Console.WriteLine($"  Junction rows for post 2 afterwards: [{string.Join(", ", slugs)}]");
}

Console.WriteLine();
Console.WriteLine("Counts are provider- and hardware-independent, so yours should match these");
Console.WriteLine("exactly. Both shapes read with a single JOIN query; the implicit junction");
Console.WriteLine("costs writes only when the collection changes, and the Attach() stub trick");
Console.WriteLine("inserts a junction row without reading anything first.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI — an unguarded ReadKey throws when stdin is redirected.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

// Every measurement gets a fresh scope, and so a fresh DbContext with an empty change
// tracker. Reusing one context would let entities loaded by an earlier strategy satisfy a
// later one from memory, and the counts would collapse to nothing.
async Task MeasureAsync(string strategy, Func<AppDbContext, Task> work)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    counter.Reset();
    await work(db);
    report.Add(strategy, counter.Count);
}

// Fail loudly rather than reporting a table of zeros against an empty database, and put
// the junction/enrollment rows back to the seeded state so every run measures the same
// work — the demos above add and remove rows on purpose.
async Task VerifySeedAndResetAsync()
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    if (!await db.Posts.AnyAsync() || !await db.Students.AnyAsync())
        throw new InvalidOperationException(
            "Seed data missing. Run seed.sql first — see this folder's README.");

    await db.Database.ExecuteSqlRawAsync("""
        DELETE FROM ManyToMany.PostTag;
        INSERT INTO ManyToMany.PostTag (PostsId, TagsId)
        VALUES (1, 1), (1, 2), (1, 3), (2, 1), (2, 3), (3, 2), (4, 3), (4, 4);

        DELETE FROM ManyToMany.StudentCourses;
        INSERT INTO ManyToMany.StudentCourses (StudentId, CourseId, EnrolledAt, FinalGrade)
        VALUES (1, 1, '2025-01-10', 0), (1, 2, '2025-01-11', 1),
               (2, 1, '2025-01-12', 0), (2, 3, '2025-01-13', NULL),
               (3, 2, '2025-01-14', 2);
        """);

    Console.WriteLine("Seed data verified; junction and enrollment rows reset to the seeded state.");
}
