using System.CommandLine;

namespace Zakira.Conduit.Cli;

/// <summary>
///     Cross-command options shared by <c>sync</c>, <c>list</c>, and <c>validate</c>.
/// </summary>
internal static class CommonOptions
{
    public static Option<FileInfo?> Manifest { get; } = new("--manifest", "-m")
    {
        Description = "Path to the conduit manifest. When omitted, the file is discovered via $XDG_CONFIG_HOME/Zakira.Conduit/conduit.json (and standard fallbacks).",
    };

    public static Option<string> Verbosity { get; } = new("--verbosity")
    {
        Description = "Output verbosity. One of: q[uiet], m[inimal], n[ormal], d[etailed], diag[nostic]. Default: normal.",
        Recursive = true,
        Arity = System.CommandLine.ArgumentArity.ZeroOrOne,
        DefaultValueFactory = _ => "normal",
    };

    public static Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Shortcut for --verbosity detailed.",
        Recursive = true,
    };

    public static Option<bool> Quiet { get; } = new("--quiet", "-q")
    {
        Description = "Shortcut for --verbosity quiet.",
        Recursive = true,
    };
}
