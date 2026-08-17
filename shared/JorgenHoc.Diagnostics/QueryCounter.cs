namespace JorgenHoc.Diagnostics;

/// <summary>
/// Counts the SQL statements EF Core actually executed.
///
/// Statement counts are the honest way to demonstrate query problems: they are
/// provider- and hardware-independent, so a reader gets the same numbers on their
/// machine. Timings are not — locally a round trip is nearly free.
/// </summary>
public sealed class QueryCounter
{
    private int _count;

    /// <remarks>
    /// Volatile read: increments happen on whichever thread EF Core's logger runs on,
    /// so a plain field read is not guaranteed to observe the latest value.
    /// </remarks>
    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Volatile.Write(ref _count, 0);
}
