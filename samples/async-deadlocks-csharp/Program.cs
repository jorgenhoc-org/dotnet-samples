using JorgenHoc.AsyncDeadlocks;

// Reproduces the deadlock described in
// https://www.jorgenhoc.org/en/blog/async-deadlocks-csharp
//
// The deadlock needs BOTH: (1) a SynchronizationContext on the calling thread, and
// (2) that thread blocked with .Result/.Wait(). Each demo toggles one ingredient.
//
// Every scenario uses the article's own diagnostic — Wait(TimeSpan) — so the deadlock
// is *observed*, never suffered: nothing in this sample hangs. The demo that deadlocks
// is un-wedged by its own timeout, and then check 2c proves the mechanism: the moment
// the thread stops blocking, the task completes on its own.

var timeout = TimeSpan.FromSeconds(2);

Console.WriteLine("1. Plain console thread — no SynchronizationContext, no deadlock possible");
Console.WriteLine("---------------------------------------------------------------------------");
Check(SynchronizationContext.Current is null, "the console main thread has no SynchronizationContext");
Check(FetchDataAsync().Wait(timeout), "blocking with .Wait()/.Result completes fine here (still bad practice — it parks a thread)");

Console.WriteLine();
Console.WriteLine("2. UI-like thread — SynchronizationContext + blocking = deadlock");
Console.WriteLine("------------------------------------------------------------------");
using (var ui = new UiLikeSynchronizationContext())
{
    var probe = BlockOnUiThread(ui, FetchDataAsync, timeout);

    Check(probe.HadContext, "the UI-like thread HAS a SynchronizationContext");
    Check(probe.TimedOut, $"deadlock: Wait({timeout.TotalSeconds:0}s) timed out — the task wants the thread, the thread waits for the task");
    // The Wait timeout released the thread; the queued continuation can finally run.
    Check(probe.Task.Wait(TimeSpan.FromSeconds(1)),
        "released the thread and the task completed immediately — it was only ever waiting for that thread");
}

Console.WriteLine();
Console.WriteLine("3. Same UI-like thread, but ConfigureAwait(false) — chain broken, no deadlock");
Console.WriteLine("-------------------------------------------------------------------------------");
using (var ui = new UiLikeSynchronizationContext())
{
    var probe = BlockOnUiThread(ui, FetchDataConfigureAwaitAsync, timeout);
    Check(probe.HadContext && !probe.TimedOut,
        "same context, same blocking call — ConfigureAwait(false) resumes on the thread pool instead");
}

Console.WriteLine();
Console.WriteLine("4. Same UI-like thread, Task.Run workaround — works, but is a code smell");
Console.WriteLine("--------------------------------------------------------------------------");
using (var ui = new UiLikeSynchronizationContext())
{
    var probe = BlockOnUiThread(ui, () => Task.Run(FetchDataAsync), timeout);
    Check(probe.HadContext && !probe.TimedOut,
        "Task.Run's await runs on a pool thread with no context to capture, so nothing needs the blocked thread");
}

Console.WriteLine();
Console.WriteLine("All checks passed. Both deadlock ingredients are necessary; remove either one and it cannot happen.");

// Keep the window open when launched from an IDE — guarded, see the n-plus-one sample.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

// The article's FetchDataAsync, with Task.Delay standing in for the HTTP call — the
// deadlock is about who resumes after the await, not about what is awaited.
static async Task<string> FetchDataAsync()
{
    await Task.Delay(100);
    return "data";
}

static async Task<string> FetchDataConfigureAwaitAsync()
{
    await Task.Delay(100).ConfigureAwait(false);
    return "data";
}

// Runs "start the fetch, then block on it" on the UI-like thread — the exact shape of
// .Result in a WinForms event handler — and reports what happened without ever letting
// the sample itself hang.
static DeadlockProbe BlockOnUiThread(
    UiLikeSynchronizationContext ui, Func<Task<string>> fetch, TimeSpan timeout)
{
    var done = new TaskCompletionSource<DeadlockProbe>(TaskCreationOptions.RunContinuationsAsynchronously);

    ui.Run(() =>
    {
        var hadContext = SynchronizationContext.Current is not null;
        var task = fetch();
        var finished = task.Wait(timeout);   // the article's "Detecting Deadlocks" pattern
        done.SetResult(new DeadlockProbe(hadContext, !finished, task));
    });

    return done.Task.GetAwaiter().GetResult();
}

static void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    Console.WriteLine($"  ok: {claim}");
}

/// <summary>What happened on the UI-like thread, reported back to the main thread.</summary>
internal sealed record DeadlockProbe(bool HadContext, bool TimedOut, Task<string> Task);
