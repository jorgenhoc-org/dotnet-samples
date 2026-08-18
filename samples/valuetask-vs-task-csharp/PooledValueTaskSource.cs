using System.Runtime.CompilerServices;

namespace JorgenHoc.ValueTaskVsTask;

/// <summary>
/// Shows when awaiting a <c>ValueTask</c> twice actually breaks.
/// </summary>
/// <remarks>
/// The default <c>AsyncValueTaskMethodBuilder</c> keeps the state machine box alive after
/// completion, so a second await usually appears to work — which is precisely what makes
/// the rule easy to violate without noticing.
///
/// <c>PoolingAsyncValueTaskMethodBuilder</c> returns the box to a pool once the result has
/// been consumed. Await twice and the second await reads a recycled object, and the runtime
/// detects it. Any <c>IValueTaskSource</c> that recycles tokens behaves the same way —
/// <c>Socket</c>, <c>System.IO.Pipelines</c> and <c>SemaphoreSlim.WaitAsync</c> all do.
///
/// So "it worked in testing" proves nothing: the same code breaks when the callee changes
/// its builder, and that is an implementation detail you do not control.
/// </remarks>
public static class PooledValueTaskSource
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> GetValueAsync()
    {
        await Task.Yield();   // force the asynchronous path — a pooled box is rented
        return 42;
    }
}
