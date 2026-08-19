using System.Collections.Concurrent;

namespace JorgenHoc.ConfigureAwaitFalse;

/// <summary>
/// A single-threaded SynchronizationContext that COUNTS every Post — the same UI-thread
/// rule WinForms/WPF impose (see samples/async-deadlocks-csharp for the plain variant),
/// plus a counter on the one method ConfigureAwait is actually about.
///
/// Every plain <c>await</c> that suspends on this context resumes via exactly one
/// <see cref="Post"/>. <c>ConfigureAwait(false)</c> skips that Post. Counting Posts
/// therefore measures precisely what ConfigureAwait(false) does and nothing else —
/// deterministically, unlike nanosecond timings.
/// </summary>
public sealed class CountingSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;
    private int _posts;

    public CountingSynchronizationContext()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "ui-like-thread" };
        _thread.Start();
    }

    /// <summary>How many continuations were posted back to this context.</summary>
    public int Posts => Volatile.Read(ref _posts);

    public void ResetCount() => Volatile.Write(ref _posts, 0);

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _posts);
        _queue.Add((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Synchronous Send is not needed for these demos.");

    /// <summary>Run <paramref name="work"/> on the single thread, like a UI event handler.</summary>
    public void Run(Action work) => _queue.Add((_ => work(), null)); // not counted — it is the demo's entry point, not an await resumption

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }

    public void Dispose() => _queue.CompleteAdding();
}
