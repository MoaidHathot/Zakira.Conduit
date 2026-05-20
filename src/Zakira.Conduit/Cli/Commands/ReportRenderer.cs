using System.Text.Json;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Renders a <see cref="SyncReport"/> to stdout either as human-friendly,
///     optionally colored, text or as machine-readable JSON.
/// </summary>
internal static class ReportRenderer
{
    public static void Render(SyncReport report, OutputFormat format, ConsoleStyle style)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(style);

        if (format == OutputFormat.Json)
        {
            RenderJson(report);
        }
        else
        {
            RenderText(report, style);
        }
    }

    private static void RenderJson(SyncReport report)
    {
        var dto = new
        {
            succeeded = report.Succeeded,
            dryRun = report.DryRun,
            exitCode = report.ExitCode,
            elapsedMs = (long)report.Elapsed.TotalMilliseconds,
            entries = report.Entries.Select(e => new
            {
                name = e.Entry.Name,
                kind = e.Entry.Source.Kind,
                skipped = e.Skipped,
                succeeded = e.Succeeded,
                resolvedRef = e.ResolvedRef,
                elapsedMs = (long)e.Elapsed.TotalMilliseconds,
                error = e.Error,
                targets = e.Targets.Select(t => new
                {
                    path = t.TargetPath,
                    succeeded = t.Succeeded,
                    filesWritten = t.FilesWritten,
                    error = t.Error,
                }),
            }),
        };

        var json = JsonSerializer.Serialize(dto, ManifestJson.WriteOptions);
        Console.WriteLine(json);
    }

    private static void RenderText(SyncReport report, ConsoleStyle style)
    {
        var totalFiles = 0;
        var failed = 0;
        var succeeded = 0;
        var skipped = 0;

        Console.WriteLine();
        Console.WriteLine(style.Bold(report.DryRun ? "Conduit dry-run report" : "Conduit sync report"));
        Console.WriteLine(new string('-', 60));

        foreach (var entry in report.Entries)
        {
            if (entry.Skipped)
            {
                skipped++;
                Console.WriteLine($"  {style.Dim("~")} {style.Dim(entry.Entry.Name)}  {style.Dim("(skipped)")}");
                continue;
            }

            if (!entry.Succeeded)
            {
                failed++;
                Console.WriteLine($"  {style.Red("X")} {entry.Entry.Name}  {style.Dim($"(failed in {entry.Elapsed.TotalSeconds:0.0}s)")}");
                if (!string.IsNullOrEmpty(entry.Error))
                {
                    Console.WriteLine($"      {style.Red("error:")} {entry.Error}");
                }
            }
            else
            {
                succeeded++;
                var refStr = string.IsNullOrEmpty(entry.ResolvedRef) ? string.Empty : style.Cyan($" @{entry.ResolvedRef}");
                Console.WriteLine($"  {style.Green("+")} {entry.Entry.Name}{refStr}  {style.Dim($"({entry.Elapsed.TotalSeconds:0.0}s)")}");
            }

            foreach (var target in entry.Targets)
            {
                var marker = target.Succeeded ? style.Green("+") : style.Red("X");
                var filesText = target.FilesWritten == 1 ? "1 file" : $"{target.FilesWritten} files";
                Console.WriteLine($"    {marker} {target.TargetPath}  {style.Dim($"({filesText})")}");
                if (!target.Succeeded && !string.IsNullOrEmpty(target.Error))
                {
                    Console.WriteLine($"        {style.Red("error:")} {target.Error}");
                }

                totalFiles += target.FilesWritten;
            }
        }

        Console.WriteLine(new string('-', 60));

        var summary = $"  {style.Green($"{succeeded} succeeded")}, {(failed == 0 ? $"{failed} failed" : style.Red($"{failed} failed"))}, {style.Dim($"{skipped} skipped")}, {totalFiles} files in {report.Elapsed.TotalSeconds:0.0}s.";
        Console.WriteLine(summary);

        if (report.DryRun)
        {
            Console.WriteLine($"  {style.Yellow("(dry-run: no targets were modified.)")}");
        }
    }
}
