using JorgenHoc.DataAccess.ValueTaskVsTask;
using JorgenHoc.ValueTaskVsTask;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // ClearProviders

// Backs https://www.jorgenhoc.org/en/blog/valuetask-vs-task-csharp
//
// Measures allocated BYTES rather than elapsed time. Allocation is what the article claims
// a difference in, and the GC reports it exactly — no statistics, no warmup variance.
//
// GC.GetTotalAllocatedBytes, deliberately not GetAllocatedBytesForCurrentThread: the latter
// is per-thread, and a cache miss suspends at `await`, so its continuation resumes on a
// different thread-pool thread and those allocations go uncounted. The per-thread counter
// silently under-reports any path that actually awaits.
//
// Seed the database first — see this folder's README.
//
//   dotnet run

const int HitIterations = 10_000;
const int MissIterations = 100;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

var connectionString = builder.Configuration.GetConnectionString("LocalDbConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'LocalDbConnection' is missing from appsettings.json.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

if (!await db.Database.CanConnectAsync())
    throw new InvalidOperationException($"Cannot connect using '{connectionString}'.");

var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
if (users.Count == 0)
    throw new InvalidOperationException("No users found. Run seed.sql first — see the README.");

Console.WriteLine($"Seed data: {users.Count} users.");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 1. The synchronous path — the case ValueTask exists for
// ---------------------------------------------------------------------------
var (taskHit, valueTaskHit) = await MeasureCacheHits();

Console.WriteLine($"Cache hit — bytes allocated per call, over {HitIterations:N0} calls");
Console.WriteLine();
Console.WriteLine("| Return type      | Bytes/call |");
Console.WriteLine("|------------------|------------|");
Console.WriteLine($"| Task<User?>      | {taskHit,10:N0} |");
Console.WriteLine($"| ValueTask<User?> | {valueTaskHit,10:N0} |");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 2. The asynchronous path — where the saving disappears into the noise
// ---------------------------------------------------------------------------
var (taskMiss, valueTaskMiss) = await MeasureCacheMisses();

Console.WriteLine($"Cache miss — bytes allocated per call, over {MissIterations:N0} calls");
Console.WriteLine();
Console.WriteLine("| Return type      | Bytes/call |");
Console.WriteLine("|------------------|------------|");
Console.WriteLine($"| Task<User?>      | {taskMiss,10:N0} |");
Console.WriteLine($"| ValueTask<User?> | {valueTaskMiss,10:N0} |");
Console.WriteLine();
Console.WriteLine("On a miss the EF Core query pipeline dominates: the wrapper choice is a");
Console.WriteLine("rounding error next to it. The saving is real only on the hit path, which is");
Console.WriteLine("why the hit ratio of your workload decides whether converting an API pays.");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 3. Awaiting twice — a correctness trap, not a performance one
// ---------------------------------------------------------------------------
await DemonstrateDoubleAwait();

// ===========================================================================

async Task<(long Task, long ValueTask)> MeasureCacheHits()
{
    var taskRepo = new TaskUserRepository(db);
    var valueRepo = new ValueTaskUserRepository(db);
    taskRepo.Warm(users);
    valueRepo.Warm(users);

    var id = users[0].Id;

    // Warm up: the first call through each path JITs its state machine, and that one-off
    // allocation would otherwise be attributed to the measurement.
    for (var i = 0; i < 100; i++)
    {
        _ = await taskRepo.GetUserAsync(id);
        _ = await valueRepo.GetUserAsync(id);
    }

    var before = GC.GetTotalAllocatedBytes(precise: true);
    for (var i = 0; i < HitIterations; i++)
        _ = await taskRepo.GetUserAsync(id);
    var taskBytes = GC.GetTotalAllocatedBytes(precise: true) - before;

    before = GC.GetTotalAllocatedBytes(precise: true);
    for (var i = 0; i < HitIterations; i++)
        _ = await valueRepo.GetUserAsync(id);
    var valueBytes = GC.GetTotalAllocatedBytes(precise: true) - before;

    return (taskBytes / HitIterations, valueBytes / HitIterations);
}

async Task<(long Task, long ValueTask)> MeasureCacheMisses()
{
    // A distinct non-existent id per iteration, so every call is a genuine miss: nothing
    // in the change tracker, nothing in the cache, so FindAsync issues a query.
    var taskRepo = new TaskUserRepository(db);
    var valueRepo = new ValueTaskUserRepository(db);

    _ = await taskRepo.GetUserAsync(900_000);   // warm up the query pipeline
    _ = await valueRepo.GetUserAsync(900_001);

    var before = GC.GetTotalAllocatedBytes(precise: true);
    for (var i = 0; i < MissIterations; i++)
        _ = await taskRepo.GetUserAsync(100_000 + i);
    var taskBytes = GC.GetTotalAllocatedBytes(precise: true) - before;

    before = GC.GetTotalAllocatedBytes(precise: true);
    for (var i = 0; i < MissIterations; i++)
        _ = await valueRepo.GetUserAsync(200_000 + i);
    var valueBytes = GC.GetTotalAllocatedBytes(precise: true) - before;

    return (taskBytes / MissIterations, valueBytes / MissIterations);
}

async Task DemonstrateDoubleAwait()
{
    Console.WriteLine("Awaiting the same ValueTask twice");
    Console.WriteLine();

    var repo = new ValueTaskUserRepository(db);
    repo.Warm(users);
    var cachedId = users[0].Id;

    // Cache hit: the ValueTask wraps a plain result, so a second await succeeds.
    var hit = repo.GetUserAsync(cachedId);
    _ = await hit;
    await Report("cache hit ", async () => _ = await hit);

    // Cache miss: now it is backed by a state machine, which is single-consumption.
    var missRepo = new ValueTaskUserRepository(db);
    var miss = missRepo.GetUserAsync(999_999);
    _ = await miss;
    await Report("cache miss", async () => _ = await miss);

    // A pooled builder recycles the state machine box once its result is consumed, so the
    // second await reads a recycled object and the runtime catches it.
    var pooled = PooledValueTaskSource.GetValueAsync();
    _ = await pooled;
    await Report("pooled    ", async () => _ = await pooled);

    // AsTask() is always safe to await repeatedly.
    var safe = new ValueTaskUserRepository(db);
    safe.Warm(users);
    var asTask = safe.GetUserAsync(cachedId).AsTask();
    _ = await asTask;
    await Report("AsTask()  ", async () => _ = await asTask);

    Console.WriteLine();
    Console.WriteLine("Note what this shows. With the default builder a second await usually");
    Console.WriteLine("SUCCEEDS — which is exactly what makes the rule so easy to break without");
    Console.WriteLine("noticing. It is undefined behaviour, not reliably an exception. Swap in a");
    Console.WriteLine("pooling builder, or await something backed by an IValueTaskSource that");
    Console.WriteLine("recycles tokens (Socket, System.IO.Pipelines, SemaphoreSlim.WaitAsync) and");
    Console.WriteLine("the same code throws. That is an implementation detail of the callee, so");
    Console.WriteLine("\"it worked in testing\" proves nothing. Call AsTask() to await twice.");

    static async Task Report(string label, Func<Task> secondAwait)
    {
        try
        {
            await secondAwait();
            Console.WriteLine($"  {label} -> second await SUCCEEDED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {label} -> second await threw {ex.GetType().Name}");
        }
    }
}
