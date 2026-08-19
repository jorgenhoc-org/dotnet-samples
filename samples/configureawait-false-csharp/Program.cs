using JorgenHoc.ConfigureAwaitFalse;

// Measures what ConfigureAwait(false) actually does, for
// https://www.jorgenhoc.org/en/blog/configureawait-false-csharp
//
// Timings for a context post are nanoseconds and drown in noise, so this counts instead:
// every plain await that suspends on a SynchronizationContext resumes via exactly one
// Post to it. Count the Posts and you have measured precisely the thing
// ConfigureAwait(false) skips — deterministically, same number on every machine.

Console.WriteLine("1. Five plain awaits on a UI-like context — five Posts back to it");
Console.WriteLine("-------------------------------------------------------------------");
using (var ui = new CountingSynchronizationContext())
{
    var r = RunOnContext(ui, async () =>
    {
        var startThread = Environment.CurrentManagedThreadId;
        for (var i = 0; i < 5; i++)
            await Task.Delay(10);                       // default = ConfigureAwait(true)
        return new Probe(ui.Posts,
            SynchronizationContext.Current is not null,
            Environment.CurrentManagedThreadId == startThread);
    });

    Check(r.Posts == 5, $"5 awaits = {r.Posts} Posts — every resumption marshalled back to the context");
    Check(r.HasContextAfter, "SynchronizationContext.Current survives every await");
    Check(r.EndedOnStartThread, "still on the UI-like thread at the end — that is what the Posts buy");
}

Console.WriteLine();
Console.WriteLine("2. Same five awaits with ConfigureAwait(false) — zero Posts");
Console.WriteLine("--------------------------------------------------------------");
using (var ui = new CountingSynchronizationContext())
{
    var r = RunOnContext(ui, async () =>
    {
        var startThread = Environment.CurrentManagedThreadId;
        for (var i = 0; i < 5; i++)
            await Task.Delay(10).ConfigureAwait(false);
        return new Probe(ui.Posts,
            SynchronizationContext.Current is not null,
            Environment.CurrentManagedThreadId == startThread);
    });

    Check(r.Posts == 0, $"5 awaits = {r.Posts} Posts — the context is never consulted again");
    Check(!r.HasContextAfter, "SynchronizationContext.Current is null after the first await — execution moved to the pool");
    Check(!r.EndedOnStartThread, "no longer on the UI-like thread — do not touch UI state after this");
}

Console.WriteLine();
Console.WriteLine("3. The propagation rule — ConfigureAwait(false) on the FIRST await only");
Console.WriteLine("--------------------------------------------------------------------------");
using (var ui = new CountingSynchronizationContext())
{
    var r = RunOnContext(ui, async () =>
    {
        await Task.Delay(10).ConfigureAwait(false);     // only this one opts out
        for (var i = 0; i < 4; i++)
            await Task.Delay(10);                       // plain awaits — but Current is already null
        return new Probe(ui.Posts, SynchronizationContext.Current is not null, false);
    });

    Check(r.Posts == 0, $"1 explicit + 4 plain awaits = {r.Posts} Posts — the plain awaits had no context left to capture");
    Check(!r.HasContextAfter, "one ConfigureAwait(false) dropped the context for the rest of the method");
}

Console.WriteLine();
Console.WriteLine("4. No context at all (console thread) — the option is irrelevant");
Console.WriteLine("-------------------------------------------------------------------");
{
    Check(SynchronizationContext.Current is null, "the console main thread has no SynchronizationContext");
    await Task.Delay(10);
    var plainStillNull = SynchronizationContext.Current is null;
    await Task.Delay(10).ConfigureAwait(false);
    var configuredStillNull = SynchronizationContext.Current is null;
    Check(plainStillNull && configuredStillNull,
        "plain await and ConfigureAwait(false) behave identically — nothing to capture, nothing to skip (ASP.NET Core is this case)");
}

Console.WriteLine();
Console.WriteLine("5. Exceptions propagate identically — ConfigureAwait does not touch error flow");
Console.WriteLine("---------------------------------------------------------------------------------");
using (var ui = new CountingSynchronizationContext())
{
    var caught = RunOnContext(ui, async () =>
    {
        try
        {
            await ThrowAfterAwaitAsync();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true; // caught at the await point, even though we resumed on the pool
        }
    });

    Check(caught, "the exception was re-thrown at the await and caught normally, despite ConfigureAwait(false)");
}

Console.WriteLine();
Console.WriteLine("All checks passed. ConfigureAwait(false) does one thing: it skips the Post.");

// Keep the window open when launched from an IDE — guarded, see the n-plus-one sample.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

static async Task ThrowAfterAwaitAsync()
{
    await Task.Delay(10).ConfigureAwait(false);
    throw new InvalidOperationException("boom");
}

// Runs an async body ON the context thread (like a UI event handler) and returns its
// result to the main thread. The wrapper's own await uses ConfigureAwait(false) so it
// never adds a Post of its own to the numbers the demos report — and each demo reads
// the counter inside the body, before the wrapper resumes, as a second guarantee.
static T RunOnContext<T>(CountingSynchronizationContext ui, Func<Task<T>> body)
{
    var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    ui.Run(() => _ = Wrap());

    return done.Task.GetAwaiter().GetResult();

    async Task Wrap()
    {
        try { done.SetResult(await body().ConfigureAwait(false)); }
        catch (Exception ex) { done.SetException(ex); }
    }
}

static void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    Console.WriteLine($"  ok: {claim}");
}

/// <summary>What a demo observed on the context thread.</summary>
internal sealed record Probe(int Posts, bool HasContextAfter, bool EndedOnStartThread);
