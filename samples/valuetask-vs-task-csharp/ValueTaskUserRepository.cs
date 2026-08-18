using JorgenHoc.DataAccess.ValueTaskVsTask;
using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.ValueTaskVsTask;

/// <summary>
/// The <c>ValueTask&lt;T&gt;</c> version of <see cref="TaskUserRepository"/>.
/// </summary>
/// <remarks>
/// Note the shape: the public method is **not** <c>async</c>. That is what makes the
/// saving real. It returns a <c>ValueTask&lt;User?&gt;</c> wrapping the cached value with
/// no state machine and no heap allocation, and only delegates to a separate <c>async</c>
/// method when it actually has to wait.
///
/// Marking this method <c>async</c> instead would allocate a state machine on every call
/// and throw the entire benefit away — the most common way this optimisation is applied
/// incorrectly.
/// </remarks>
public sealed class ValueTaskUserRepository(AppDbContext db)
{
    private readonly Dictionary<int, User?> _cache = [];

    public int CacheCount => _cache.Count;

    public ValueTask<User?> GetUserAsync(int id)
    {
        if (_cache.TryGetValue(id, out var cached))
            return new ValueTask<User?>(cached);   // struct — no heap allocation

        return FetchAndCacheAsync(id);
    }

    private async ValueTask<User?> FetchAndCacheAsync(int id)
    {
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
