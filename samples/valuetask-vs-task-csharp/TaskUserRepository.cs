using JorgenHoc.DataAccess.ValueTaskVsTask;
using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.ValueTaskVsTask;

/// <summary>
/// The <c>Task&lt;T&gt;</c> version. Deliberately the same method name and signature shape
/// as <see cref="ValueTaskUserRepository"/> so the two can be compared line for line —
/// only the return type differs, which is the entire point.
/// </summary>
/// <remarks>
/// A plain <c>Dictionary</c> rather than <c>IMemoryCache</c>: the measurements here are in
/// bytes allocated per call, and IMemoryCache allocates internally on both hit and miss.
/// That noise would swamp the ~72 bytes we are trying to observe.
/// </remarks>
public sealed class TaskUserRepository(AppDbContext db)
{
    private readonly Dictionary<int, User?> _cache = [];

    public int CacheCount => _cache.Count;

    // `async` plus a cached return still allocates: the compiler builds a state machine and
    // the method hands back a Task<User?> even though it never suspends.
    public async Task<User?> GetUserAsync(int id)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        var user = await db.Users.FindAsync(id);
        _cache[id] = user;
        return user;
    }

    public void Warm(IEnumerable<User> users)
    {
        foreach (var u in users)
            _cache[u.Id] = u;
    }
}
