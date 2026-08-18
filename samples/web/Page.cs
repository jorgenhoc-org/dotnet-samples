using StackExchange.Profiling;

namespace JorgenHoc.SampleWeb;

/// <summary>
/// Minimal HTML shell. Every page must go through <see cref="Shell"/> so the MiniProfiler
/// include script lands in the markup — the overlay does not appear without it, and a
/// missing script tag is indistinguishable from a broken profiler.
/// </summary>
public static class Page
{
    // $$""" so that a single brace is literal (the CSS) and {{...}} interpolates.
    public static string Shell(HttpContext ctx, string title, string body)
    {
        // RenderIncludes emits the <script> tag that fetches the profiler UI.
        var profiler = MiniProfiler.Current?.RenderIncludes(ctx).ToString() ?? "";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{{title}} — JorgenHoc samples</title>
              <style>
                :root { color-scheme: light dark; }
                body { font: 15px/1.6 ui-sans-serif, system-ui, sans-serif;
                       max-width: 60rem; margin: 2rem auto; padding: 0 1rem; }
                h1 { font-size: 1.4rem; }
                h2 { font-size: 1.1rem; }
                code { font-family: ui-monospace, monospace; }
                table { border-collapse: collapse; width: 100%; }
                td, th { text-align: left; padding: .3rem .6rem;
                         border-bottom: 1px solid rgba(128,128,128,.35); }
                .hint { opacity: .75; font-size: .9rem; }
                a { color: inherit; }
              </style>
              {{profiler}}
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }

    public static string Index(HttpContext ctx) => Shell(ctx, "Samples", """
        <h1>JorgenHoc web samples</h1>
        <p class="hint">
          Each link runs a request against the seeded sample database with MiniProfiler
          enabled. Open the badge in the top-left corner after the page loads to see every
          SQL statement that request executed.
        </p>

        <h2>EF Core: the N+1 query problem</h2>
        <ul>
          <li><a href="/ef-core-n-plus-one/orders-nplus1">orders-nplus1</a> — one query per row</li>
          <li><a href="/ef-core-n-plus-one/orders-fixed">orders-fixed</a> — single projected query</li>
        </ul>
        <p class="hint">
          Both accept <code>?orders=N</code> (default 500). Load them one after the other and
          compare the query count in the profiler badge.
        </p>
        """);
}
