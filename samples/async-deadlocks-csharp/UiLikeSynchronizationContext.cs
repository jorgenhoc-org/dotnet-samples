using System.Collections.Concurrent;

namespace JorgenHoc.AsyncDeadlocks;

/// <summary>
/// A minimal single-threaded SynchronizationContext — the same shape WinForms and WPF
/// give you: every posted callback runs on ONE dedicated thread, in order.
///
/// This is what makes the deadlock reproducible in a console app. A plain console app
/// has no SynchronizationContext (that is demo 1), so the classic UI/classic-ASP.NET
/// deadlock physically cannot happen there. Installing this context recreates the
/// "all continuations must come back to this one thread" rule that the deadlock needs.
/// </summary>
public sealed class UiLikeSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;

    public UiLikeSynchronizationContext()
    {
        // Background: the deadlock demo leaves this thread blocked on purpose, and a
        // foreground thread would keep the process alive forever afterwards.
        _thread = new Thread(Pump) { IsBackground = true, Name = "ui-like-thread" };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Synchronous Send is not needed for these demos.");

    /// <summary>Run <paramref name="work"/> on the single thread, like a UI event handler.</summary>
    public void Run(Action work) => Post(_ => work(), null);

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }

    public void Dispose() => _queue.CompleteAdding();
}
