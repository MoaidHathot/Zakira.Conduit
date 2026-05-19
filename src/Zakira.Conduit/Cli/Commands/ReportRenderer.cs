using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Cli.Commands;

/// <summary>
///     Pretty-prints a <see cref="SyncReport"/> to stdout.
/// </summary>
internal static class ReportRenderer
{
    public static void Render(SyncReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var totalFiles = 0;
        var failed = 0;
        var succeeded = 0;
        var skipped = 0;

        Console.WriteLine();
        Console.WriteLine(report.DryRun ? "Conduit dry-run report" : "Conduit sync report");
        Console.WriteLine(new string('-', 60));

        foreach (var entry in report.Entries)
        {
            if (entry.Skipped)
            {
                skipped++;
                Console.WriteLine($"  ~ {entry.Entry.Name}  (skipped)");
                continue;
            }

            if (!entry.Succeeded)
            {
                failed++;
                Console.WriteLine($"  X {entry.Entry.Name}  (failed in {entry.Elapsed.TotalSeconds:0.0}s)");
                if (!string.IsNullOrEmpty(entry.Error))
                {
                    Console.WriteLine($"      error: {entry.Error}");
                }
            }
            else
            {
                succeeded++;
                var refStr = string.IsNullOrEmpty(entry.ResolvedRef) ? string.Empty : $" @{entry.ResolvedRef}";
                Console.WriteLine($"  + {entry.Entry.Name}{refStr}  ({entry.Elapsed.TotalSeconds:0.0}s)");
            }

            foreach (var target in entry.Targets)
            {
                var prefix = target.Succeeded ? "    +" : "    X";
                Console.WriteLine($"{prefix} {target.TargetPath}  ({target.FilesWritten} file{(target.FilesWritten == 1 ? string.Empty : "s")})");
                if (!target.Succeeded && !string.IsNullOrEmpty(target.Error))
                {
                    Console.WriteLine($"        error: {target.Error}");
                }

                totalFiles += target.FilesWritten;
            }
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"  {succeeded} succeeded, {failed} failed, {skipped} skipped, {totalFiles} files in {report.Elapsed.TotalSeconds:0.0}s.");
        if (report.DryRun)
        {
            Console.WriteLine("  (dry-run: no targets were modified.)");
        }
    }
}
