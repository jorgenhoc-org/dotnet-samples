using System.Globalization;

namespace JorgenHoc.Diagnostics;

/// <summary>
/// Collects "strategy → statements executed" rows and prints them as a Markdown table,
/// so output can be pasted straight into an article without retyping numbers.
/// </summary>
public sealed class StatementReport(string title)
{
    private readonly List<(string Strategy, int Statements)> _rows = [];

    public void Add(string strategy, int statements) => _rows.Add((strategy, statements));

    public void Print(TextWriter? writer = null)
    {
        writer ??= Console.Out;

        if (_rows.Count == 0)
        {
            writer.WriteLine("(no measurements recorded)");
            return;
        }

        // Size the first column to its widest value so the table stays aligned as
        // strategy names change.
        var width = Math.Max("Strategy".Length, _rows.Max(r => r.Strategy.Length));

        writer.WriteLine();
        writer.WriteLine(title);
        writer.WriteLine();
        writer.WriteLine($"| {"Strategy".PadRight(width)} | SQL statements |");
        writer.WriteLine($"|{new string('-', width + 2)}|----------------|");

        foreach (var (strategy, statements) in _rows)
        {
            var count = statements.ToString("N0", CultureInfo.InvariantCulture);
            writer.WriteLine($"| {strategy.PadRight(width)} | {count,14} |");
        }

        writer.WriteLine();
    }
}
