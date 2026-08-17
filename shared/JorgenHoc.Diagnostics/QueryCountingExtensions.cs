using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JorgenHoc.Diagnostics;

public static class QueryCountingExtensions
{
    /// <summary>
    /// Registers a logger that counts executed SQL commands, and optionally prints them.
    /// </summary>
    /// <remarks>
    /// Two traps this exists to avoid.
    ///
    /// First, the filter matches <see cref="RelationalEventId.CommandExecuted"/> exactly.
    /// The simpler <c>LogTo(Action&lt;string&gt;, LogLevel)</c> overload counts every log
    /// message at that level — connection and transaction events included — which
    /// inflates the total by a handful and makes the number impossible to reason about.
    ///
    /// Second, counting and printing happen in the same logger. EF Core writes through
    /// <c>ILoggerFactory</c>, so a host with the default console provider prints every
    /// statement whether or not you called <c>LogTo</c> — which makes logging look wired
    /// up when it is not. Call <c>builder.Logging.ClearProviders()</c> so what you see is
    /// what you configured.
    /// </remarks>
    public static DbContextOptionsBuilder CountStatements(
        this DbContextOptionsBuilder options,
        QueryCounter counter,
        bool printSql = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(counter);

        return options.LogTo(
            filter: (eventId, _) => eventId == RelationalEventId.CommandExecuted,
            logger: eventData =>
            {
                counter.Increment();
                if (printSql)
                    Console.WriteLine(eventData.ToString());
            });
    }
}
