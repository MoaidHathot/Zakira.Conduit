using System.Diagnostics;
using System.Reflection;

namespace Zakira.Conduit.E2ETests.TestHelpers;

/// <summary>
///     Locates the freshly-built <c>conduit</c> CLI assembly and runs it as a
///     subprocess. Uses <c>dotnet exec &lt;dll&gt;</c> so the same code runs
///     cross-platform without needing a published native binary.
/// </summary>
internal static class ConduitCli
{
    /// <summary>
    ///     Absolute path to the built <c>conduit.dll</c>. Resolved off the
    ///     test assembly's location (which sits next to the CLI's bin output
    ///     thanks to the project reference declared in the E2E csproj).
    /// </summary>
    public static string AssemblyPath { get; } = ResolveAssemblyPath();

    public sealed record Result(int ExitCode, string StdOut, string StdErr)
    {
        public string Combined => StdOut + StdErr;
    }

    public static async Task<Result> RunAsync(
        IEnumerable<string> args,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(AssemblyPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Inherit current environment, then apply overrides. A null value removes the var.
        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                if (value is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = value;
                }
            }
        }

        using var process = new Process { StartInfo = psi };
        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"conduit CLI process timed out. stdout:\n{stdOut}\nstderr:\n{stdErr}");
        }

        return new Result(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static string ResolveAssemblyPath()
    {
        // The CLI dll is named conduit.dll and is referenced as a project; its
        // output sits next to ours. If the CLI hasn't been built yet (e.g. when
        // running an individual test from an IDE), fall back to a side-by-side
        // path inside the repository.
        var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                      ?? throw new InvalidOperationException("Cannot determine test assembly directory.");

        var sideBySide = Path.Combine(testDir, "conduit.dll");
        if (File.Exists(sideBySide))
        {
            return sideBySide;
        }

        var repoRoot = FindRepoRoot(testDir)
                       ?? throw new InvalidOperationException("Cannot locate repository root from test output directory.");

        // ../../../../../src/Zakira.Conduit/bin/{config}/{tfm}/conduit.dll
        var configDir = Path.GetFileName(Path.GetDirectoryName(testDir)!);
        var tfm = Path.GetFileName(testDir);
        var probe = Path.Combine(repoRoot, "src", "Zakira.Conduit", "bin", configDir!, tfm!, "conduit.dll");
        if (File.Exists(probe))
        {
            return probe;
        }

        throw new FileNotFoundException(
            $"conduit.dll not found. Tried '{sideBySide}' and '{probe}'. Build the CLI project first.",
            sideBySide);
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Zakira.Conduit.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "Zakira.Conduit.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
