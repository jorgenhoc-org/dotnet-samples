using System.Collections.Concurrent;
using static JorgenHoc.AsyncVsTaskRun.Workloads;

// Asserted behaviour for every claim in
// https://www.jorgenhoc.org/en/blog/async-await-vs-task-run
//
// Each check throws if it fails. The distinction the article turns on — I/O-bound work
// wants async/await, CPU-bound work wants Task.Run — is made observable through thread
// ids and completion state, not asserted in prose.

// Grow the pool up front so the parallelism check below isn't throttled by the pool's
// one-new-thread-per-500ms ramp; without this, 8 concurrent tasks would trickle out.
ThreadPool.SetMinThreads(Math.Max(16, Environment.ProcessorCount * 2), 16);

var callerThread = Environment.CurrentManagedThreadId;
var checksPassed = 0;

Console.WriteLine($"Caller (main) thread id: {callerThread}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// CPU-bound: a direct call stays on the caller's thread; Task.Run offloads
// ---------------------------------------------------------------------------

Console.WriteLine("CPU-bound work: direct call vs Task.Run");
{
    var (directResult, directThread) = Compute(200_000);
    Check(directThread == callerThread,
        "a direct synchronous call runs the CPU work on the caller's OWN thread (blocking it)");

    var (offloadResult, offloadThread) = await Task.Run(() => Compute(200_000));
    Check(offloadThread != callerThread,
        "Task.Run offloaded the identical work to a different thread-pool thread, freeing the caller");
    Check(directResult == offloadResult, "both paths produced the identical result");
}

// ---------------------------------------------------------------------------
// "Fake async" runs synchronously; a real async method yields
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Fake async (Task.FromResult) vs real async (awaits)");
{
    var fakeBodyThread = -1;

    // The classic anti-pattern: synchronous work, then Task.FromResult. The body runs
    // NOW, on the caller's thread, before any Task exists.
    Task<int> FakeCountAsync()
    {
        Thread.SpinWait(50_000);                       // synchronous CPU work
        fakeBodyThread = Environment.CurrentManagedThreadId;
        return Task.FromResult(42);
    }

    // Capture the thread executing right here — after the earlier awaits this is a pool
    // thread, not the main thread. A synchronous call runs inline on whatever thread is
    // current, so that is what "ran synchronously" must be compared against.
    var inlineThread = Environment.CurrentManagedThreadId;
    var fakeTask = FakeCountAsync();
    Check(fakeTask.IsCompletedSuccessfully,
        "\"fake async\" returned an already-completed task — it never yielded");
    Check(fakeBodyThread == inlineThread,
        "...and its body ran synchronously on the calling thread, blocking it the whole time");

    // A real async method that hits an actual await returns BEFORE finishing.
    var realTask = RealAsync();
    Check(!realTask.IsCompleted,
        "a real async method awaiting Task.Yield() returned before completing — it yielded the thread");
    await realTask;
}

// ---------------------------------------------------------------------------
// Fire-and-forget: unawaited exceptions are invisible until observed
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Fire-and-forget exception handling");
{
    var faulting = Task.Run(() => throw new InvalidOperationException("unobserved boom"));

    // Let it fault, then keep going WITHOUT awaiting it.
    try { await Task.Delay(50); } catch { /* unreachable */ }
    Check(faulting.IsFaulted,
        "the fire-and-forget task faulted, yet the caller ran on none the wiser — nothing propagated");

    // The exception was there all along; awaiting surfaces it (and observes it, so the
    // finalizer won't later raise UnobservedTaskException).
    var surfaced = false;
    try { await faulting; }
    catch (InvalidOperationException) { surfaced = true; }
    Check(surfaced, "awaiting the same task finally surfaces the exception the caller had ignored");

    // The article's safer alternative: ContinueWith on the default scheduler observes it.
    Exception? captured = null;
    var faulting2 = Task.Run(() => throw new InvalidOperationException("continuation boom"));
    await faulting2.ContinueWith(
        t => { if (t.IsFaulted) captured = t.Exception!.InnerException; },
        TaskScheduler.Default);
    Check(captured is InvalidOperationException,
        "ContinueWith(TaskScheduler.Default) observed the fault and captured the exception");
}

// ---------------------------------------------------------------------------
// The recommended reliable pattern: Channel + BackgroundService-style loop
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Channel-backed background processing");
{
    var (processed, failures) = await RunEmailQueueAsync(
    [
        new EmailMessage("a@example.com", FailToSend: false),
        new EmailMessage("b@example.com", FailToSend: true),   // throws mid-stream
        new EmailMessage("c@example.com", FailToSend: false),
    ]);

    Check(processed.SequenceEqual(["a@example.com", "c@example.com"]) && failures == 1,
        "one message threw, but the consumer loop survived and still delivered the other two (failures=1)");
}

// ---------------------------------------------------------------------------
// Parallel CPU work genuinely spreads across threads
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Parallel CPU via Task.WhenAll(Task.Run(...))");
{
    const int degree = 8;
    var threadIds = new ConcurrentBag<int>();

    // A barrier forces all eight to run at the same instant — otherwise the pool could
    // satisfy them one after another on a single reused thread and the count would lie.
    using var barrier = new Barrier(degree);

    var results = await Task.WhenAll(Enumerable.Range(0, degree).Select(_ => Task.Run(() =>
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("thread pool did not supply enough threads");
        var (result, threadId) = Compute(200_000);
        threadIds.Add(threadId);
        return result;
    })));

    Check(results.Distinct().Count() == 1 && results.Length == degree,
        $"all {degree} parallel tasks produced the same correct result");
    Check(threadIds.Distinct().Count() == degree,
        $"...each on its own thread-pool thread ({degree} distinct threads, none of them the caller)");
    Check(!threadIds.Contains(callerThread), "none of the parallel work touched the caller's thread");
}

Console.WriteLine();
Console.WriteLine($"All {checksPassed} checks passed. The rule holds: await I/O directly, hand CPU");
Console.WriteLine("work to Task.Run, and never let a fire-and-forget task swallow its own failure.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI — an unguarded ReadKey throws when stdin is redirected.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    checksPassed++;
    Console.WriteLine($"  [OK] {claim}");
}
