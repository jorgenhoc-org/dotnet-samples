using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Proves the exception-propagation claims in
// https://www.jorgenhoc.org/en/blog/async-exception-handling-csharp
//
// Exception routing is fully observable: which catch block ran, what type came out of
// the await, what state the task ended in, what a stack trace contains. Each demo
// asserts one of those outcomes. Demo 4 re-launches this same executable as a child
// process to observe an async void crash from the outside without killing this run.

// ---- child mode for demo 4: crash via async void with NO SynchronizationContext ----
if (args is ["--async-void-crash"])
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        Console.WriteLine($"UNHANDLED_EVENT fired IsTerminating={e.IsTerminating} " +
                          $"Type={(e.ExceptionObject as Exception)?.GetType().Name}");

    SynchronizationContext.SetSynchronizationContext(null); // console default, but be explicit
    CrashingAsyncVoid();

    Thread.Sleep(10_000); // the rethrown thread-pool exception must kill us long before this
    Console.WriteLine("STILL_ALIVE"); // must never print
    return;

    static async void CrashingAsyncVoid()
    {
        await Task.Yield(); // continuation lands on the thread pool
        throw new InvalidOperationException("escaped from async void");
    }
}

Console.WriteLine("1. await re-throws the ORIGINAL exception; the task itself wraps it in AggregateException");
Console.WriteLine("--------------------------------------------------------------------------------------------");
{
    var task = FaultingMethodAsync();
    Exception? caught = null;
    try { await task; }
    catch (Exception ex) { caught = ex; }

    Check(caught is InvalidOperationException, "await threw the original InvalidOperationException, not an AggregateException");
    Check(task.Status == TaskStatus.Faulted, "the task ended Faulted");
    Check(task.Exception is AggregateException agg && agg.InnerExceptions is [InvalidOperationException],
        "task.Exception is an AggregateException wrapping that same single exception");
    Check(caught!.StackTrace!.Contains(nameof(FaultingMethodAsync)),
        "the re-thrown exception still carries the async method's frame in its stack trace");
}

Console.WriteLine();
Console.WriteLine("2. A throw BEFORE the first await is captured in the task, not thrown at the call site");
Console.WriteLine("-----------------------------------------------------------------------------------------");
{
    Task? task = null;
    var threwAtCallSite = false;
    try { task = ThrowsBeforeFirstAwaitAsync(null!); }
    catch (ArgumentNullException) { threwAtCallSite = true; }
    Check(!threwAtCallSite, "calling the async method did NOT throw synchronously");

    var caughtAtAwait = false;
    try { await task!; }
    catch (ArgumentNullException) { caughtAtAwait = true; }
    Check(caughtAtAwait, "the ArgumentNullException surfaced only when the task was awaited");

    // The BCL-style fix: a synchronous wrapper validates eagerly.
    var wrapperThrew = false;
    try { _ = ValidatingWrapperAsync(null!); } // note: NOT awaited
    catch (ArgumentNullException) { wrapperThrew = true; }
    Check(wrapperThrew, "the non-async validating wrapper throws at the call site, before any await");
}

Console.WriteLine();
Console.WriteLine("3. async void: the exception is posted to the SynchronizationContext, not to the caller");
Console.WriteLine("------------------------------------------------------------------------------------------");
{
    var recorder = new RecordingSynchronizationContext();
    var previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(recorder);
    try
    {
        var caughtAtCallSite = false;
        try
        {
            AsyncVoidThatThrows(); // returns void — nothing for the caller to observe
        }
        catch (InvalidOperationException) { caughtAtCallSite = true; }

        Check(!caughtAtCallSite, "try/catch around the async void CALL caught nothing");
        Check(recorder.CapturedExceptions is [InvalidOperationException],
            "the exception was raised via SynchronizationContext.Post on the context active at the start");
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
    }
}

Console.WriteLine();
Console.WriteLine("4. async void with NO SynchronizationContext = ordinary unhandled thread-pool exception");
Console.WriteLine("------------------------------------------------------------------------------------------");
{
    // Child process re-runs this executable with --async-void-crash (see top of file).
    var psi = new ProcessStartInfo(Environment.ProcessPath!, "--async-void-crash")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var child = Process.Start(psi)!;
    var childOut = await child.StandardOutput.ReadToEndAsync();
    var childErr = await child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync();

    Check(child.ExitCode != 0, $"the child process died (exit code {child.ExitCode})");
    Check(!childOut.Contains("STILL_ALIVE"), "it never reached the code after the async void call chain");
    Check(childOut.Contains("UNHANDLED_EVENT fired IsTerminating=True"),
        "AppDomain.UnhandledException FIRED with IsTerminating=true — so this is the normal " +
        "unhandled-exception path, NOT Environment.FailFast (FailFast skips that handler)");
    Check(childErr.Contains("Unhandled exception") && childErr.Contains("escaped from async void"),
        "the runtime printed the standard 'Unhandled exception' banner with our exception");
}

Console.WriteLine();
Console.WriteLine("5. Task.WhenAll: await gives you ONE exception, the WhenAll task holds ALL of them");
Console.WriteLine("------------------------------------------------------------------------------------");
{
    var ranToEnd = false;
    var tasks = new[]
    {
        Task.FromException(new ArgumentException("Error A")),
        Task.FromException(new InvalidOperationException("Error B")),
        Task.Run(async () => { await Task.Delay(50); ranToEnd = true; }),
    };

    var whenAll = Task.WhenAll(tasks); // keep the combined task BEFORE awaiting
    Exception? caught = null;
    try { await whenAll; }
    catch (Exception ex) { caught = ex; }

    Check(caught is ArgumentException, "await re-threw only the FIRST exception (ArgumentException 'Error A')");
    Check(caught is not AggregateException, "…and it is not an AggregateException");
    Check(ranToEnd && tasks[2].Status == TaskStatus.RanToCompletion,
        "WhenAll still waited for the healthy task to finish despite the early failures");
    Check(whenAll.Exception!.InnerExceptions.Count == 2
        && whenAll.Exception.InnerExceptions[0] is ArgumentException
        && whenAll.Exception.InnerExceptions[1] is InvalidOperationException,
        "whenAll.Exception.InnerExceptions holds BOTH failures — nothing is lost if you kept the reference");
}

Console.WriteLine();
Console.WriteLine("6. Nested AggregateException and Flatten()");
Console.WriteLine("--------------------------------------------");
{
    // Attached child tasks are the classic source of genuinely nested aggregates.
    var parent = Task.Factory.StartNew(() =>
    {
        Task.Factory.StartNew(
            () => throw new InvalidOperationException("from attached child"),
            TaskCreationOptions.AttachedToParent);
    });

    AggregateException? caught = null;
    try { parent.Wait(); }
    catch (AggregateException ex) { caught = ex; }

    Check(caught!.InnerExceptions is [AggregateException],
        "the parent's AggregateException contains ANOTHER AggregateException, not the real error");
    Check(caught.Flatten().InnerExceptions is [InvalidOperationException],
        "Flatten() collapses the nesting down to the actual InvalidOperationException");

    // WhenAll-of-WhenAll, by contrast, does NOT nest: WhenAll re-aggregates leaf exceptions.
    var inner = Task.WhenAll(
        Task.FromException(new ArgumentException("A")),
        Task.FromException(new InvalidOperationException("B")));
    var outer = Task.WhenAll(inner, Task.FromException(new TimeoutException("C")));
    try { await outer; } catch { /* observed via outer.Exception below */ }

    Check(outer.Exception!.InnerExceptions.Count == 3
        && outer.Exception.InnerExceptions.All(e => e is not AggregateException),
        "nested WhenAll stays flat: the outer task exposes all 3 leaf exceptions directly");
}

Console.WriteLine();
Console.WriteLine("7. Task.WhenAny returns the first COMPLETED task — a faulted task can be the winner");
Console.WriteLine("--------------------------------------------------------------------------------------");
{
    var pending = Task.Delay(5_000).ContinueWith(_ => "slow result");
    var alreadyFaulted = Task.FromException<string>(new InvalidOperationException("fast failure"));

    var winner = await Task.WhenAny(pending, alreadyFaulted);

    Check(ReferenceEquals(winner, alreadyFaulted),
        "the ALREADY-FAULTED task won the race — WhenAny does not mean 'first success'");
    Check(true, "await Task.WhenAny(...) itself did not throw, even though the winner is faulted");

    var thrownOnUnwrap = false;
    try { _ = await winner; }
    catch (InvalidOperationException) { thrownOnUnwrap = true; }
    Check(thrownOnUnwrap, "the exception surfaces only when you await the winner itself");
}

Console.WriteLine();
Console.WriteLine("8. Exception filters: 'when (LogAndReturnFalse(ex))' observes without catching");
Console.WriteLine("--------------------------------------------------------------------------------");
{
    var filterSawException = false;
    var originalCaughtDownstream = false;
    var thrown = new InvalidOperationException("filtered");

    try
    {
        try
        {
            throw thrown;
        }
        catch (Exception ex) when (LogAndReturnFalse(ex))
        {
            Check(false, "unreachable — the filter returned false, so this block must never run");
        }
    }
    catch (Exception ex)
    {
        originalCaughtDownstream = ReferenceEquals(ex, thrown);
    }

    Check(filterSawException, "the filter ran and observed the exception");
    Check(originalCaughtDownstream, "the SAME exception instance kept propagating to the outer catch");

    bool LogAndReturnFalse(Exception ex)
    {
        filterSawException = ReferenceEquals(ex, thrown);
        return false; // never catch — pure observation
    }
}

Console.WriteLine();
Console.WriteLine("9. Canceled vs Faulted: where the OperationCanceledException came from matters");
Console.WriteLine("---------------------------------------------------------------------------------");
{
    // 9a. An async method that throws OCE ends Canceled — the builder special-cases OCE.
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var canceled = ThrowsOceAsync(cts.Token);
    var caughtToken = CancellationToken.None;
    try { await canceled; }
    catch (OperationCanceledException ex) { caughtToken = ex.CancellationToken; }

    Check(canceled.Status == TaskStatus.Canceled, "async method throwing OCE(cancelled token) => task is Canceled, not Faulted");
    Check(caughtToken == cts.Token, "the OCE carries the token, so the caller can tell WHOSE cancellation it was");

    // 9b. The builder special-cases ANY OCE — even one whose token was never cancelled.
    using var neverCancelled = new CancellationTokenSource();
    var stillCanceled = ThrowsOceAsync(neverCancelled.Token);
    try { await stillCanceled; } catch (OperationCanceledException) { }
    Check(stillCanceled.Status == TaskStatus.Canceled,
        "an async method throwing OCE ends Canceled even when its token was NEVER cancelled");

    // 9c. A SYNCHRONOUS Task.Run delegate throwing OCE with no matching token => Faulted.
    var syncDelegate = Task.Run((Action)(() => throw new OperationCanceledException("no token involved")));
    try { await syncDelegate; } catch (OperationCanceledException) { }
    Check(syncDelegate.Status == TaskStatus.Faulted,
        "Task.Run(sync Action) throwing OCE without a matching token => Faulted, not Canceled");

    // 9d. …unless the OCE's token IS the token passed to Task.Run, and it is cancelled.
    using var matching = new CancellationTokenSource();
    var tokenMatched = Task.Run(
        (Action)(() => { matching.Cancel(); throw new OperationCanceledException(matching.Token); }),
        matching.Token);
    try { await tokenMatched; } catch (OperationCanceledException) { }
    Check(tokenMatched.Status == TaskStatus.Canceled,
        "the same sync throw ends Canceled when the OCE's token matches the one given to Task.Run");
}

Console.WriteLine();
Console.WriteLine("10. Stack traces: throw; and ExceptionDispatchInfo preserve them, throw ex; destroys them");
Console.WriteLine("--------------------------------------------------------------------------------------------");
{
    // throw; keeps the original frame
    try { RethrowWithBareThrow(); }
    catch (InvalidOperationException ex)
    {
        Check(ex.StackTrace!.Contains(nameof(ThrowFromDeepInside)),
            "after 'throw;' the trace still contains the original throwing method");
    }

    // throw ex; resets the trace to the rethrow site
    try { RethrowWithThrowEx(); }
    catch (InvalidOperationException ex)
    {
        Check(!ex.StackTrace!.Contains(nameof(ThrowFromDeepInside)),
            "after 'throw ex;' the original throwing method is GONE from the trace");
    }

    // ExceptionDispatchInfo: capture in one place, rethrow in another, trace survives
    ExceptionDispatchInfo? captured = null;
    try { ThrowFromDeepInside(); }
    catch (InvalidOperationException ex) { captured = ExceptionDispatchInfo.Capture(ex); }

    try { RethrowElsewhere(captured!); }
    catch (InvalidOperationException ex)
    {
        Check(ex.StackTrace!.Contains(nameof(ThrowFromDeepInside)),
            "EDI.Throw() re-threw with the original method still in the trace");
        Check(ex.StackTrace.Contains(nameof(RethrowElsewhere)),
            "…and the rethrow site's frame was appended after the original frames");
        // The famous '--- End of stack trace from previous location ---' seam line is
        // NOT universal: it renders when the catch lives in a synchronous method, but
        // when the surrounding method is async (like this top-level program, which
        // awaits), the segments are stitched together without the seam literal.
        // Both original and rethrow frames survive either way — that is the guarantee.
    }
}

Console.WriteLine();
Console.WriteLine("11. TaskScheduler.UnobservedTaskException fires when a dropped faulted task is GC'd");
Console.WriteLine("--------------------------------------------------------------------------------------");
{
    var fired = new TaskCompletionSource<AggregateException>();
    EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
    {
        e.SetObserved(); // keep the event from being raised again for these exceptions
        if (e.Exception.InnerExceptions.Any(x => x.Message == "unobserved-demo"))
            fired.TrySetResult(e.Exception);
    };
    TaskScheduler.UnobservedTaskException += handler;
    try
    {
        CreateAndDropFaultedTask(); // NoInlining, so no live reference survives on the stack

        // The event is raised by the exception holder's finalizer, so force a full GC cycle.
        AggregateException? observed = null;
        for (var i = 0; i < 5 && observed is null; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (fired.Task.IsCompleted) observed = await fired.Task;
        }

        Check(observed is not null, "the handler fired during garbage collection, not when the task faulted");
        Check(observed!.InnerExceptions is [InvalidOperationException],
            "args.Exception is an AggregateException wrapping the dropped task's exception");
    }
    finally
    {
        TaskScheduler.UnobservedTaskException -= handler;
    }
}

Console.WriteLine();
Console.WriteLine("All checks passed. Exceptions never disappear — they are stored, posted, or finalized; the only question is where.");

// Keep the window open when launched from an IDE — guarded, see the n-plus-one sample.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

static void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    Console.WriteLine($"  ok: {claim}");
}

static async Task FaultingMethodAsync()
{
    await Task.Yield();
    throw new InvalidOperationException("stored in the task");
}

// CA1510 suggests ArgumentNullException.ThrowIfNull — kept as explicit throws so the
// code matches the article's snippets exactly.
#pragma warning disable CA1510
static async Task ThrowsBeforeFirstAwaitAsync(string input)
{
    if (input is null) throw new ArgumentNullException(nameof(input));
    await Task.Delay(10);
}

static Task ValidatingWrapperAsync(string input)
{
    if (input is null) throw new ArgumentNullException(nameof(input)); // eager — before any state machine
    return ThrowsBeforeFirstAwaitAsync(input);
}
#pragma warning restore CA1510

static async void AsyncVoidThatThrows()
{
    await Task.Yield(); // captures the current SynchronizationContext
    throw new InvalidOperationException("raised on the SynchronizationContext");
}

static async Task ThrowsOceAsync(CancellationToken token)
{
    await Task.Yield();
    throw new OperationCanceledException(token);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void ThrowFromDeepInside() => throw new InvalidOperationException("deep");

static void RethrowWithBareThrow()
{
    try { ThrowFromDeepInside(); }
    catch { throw; }
}

static void RethrowWithThrowEx()
{
    try { ThrowFromDeepInside(); }
#pragma warning disable CA2200 // deliberately demonstrating the anti-pattern
    catch (InvalidOperationException ex) { throw ex; }
#pragma warning restore CA2200
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void RethrowElsewhere(ExceptionDispatchInfo captured) => captured.Throw();

[MethodImpl(MethodImplOptions.NoInlining)]
static void CreateAndDropFaultedTask()
{
    _ = Task.FromException(new InvalidOperationException("unobserved-demo"));
}

// Executes posts inline and records what async void machinery throws through Post.
internal sealed class RecordingSynchronizationContext : SynchronizationContext
{
    public List<Exception> CapturedExceptions { get; } = [];

    public override void Post(SendOrPostCallback d, object? state)
    {
        try { d(state); }
        catch (Exception ex) { CapturedExceptions.Add(ex); }
    }
}
