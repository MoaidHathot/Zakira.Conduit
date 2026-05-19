using Microsoft.Extensions.Logging;

namespace Zakira.Conduit.Cli;

/// <summary>
///     Pre-parses the <c>--verbosity</c> / <c>-v</c> / <c>-q</c> tokens out of
///     <see cref="System.Environment.GetCommandLineArgs"/> so the logging
///     pipeline can be configured before the System.CommandLine parser runs.
///     Anything unrecognized is silently ignored: it will be reported as a
///     parse error by System.CommandLine in the normal pipeline.
/// </summary>
internal static class VerbosityParser
{
    public static LogLevel ParseFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-q" or "--quiet")
            {
                return LogLevel.Warning;
            }

            if (arg is "-v" or "--verbose")
            {
                return LogLevel.Debug;
            }

            if (arg is "--verbosity" && i + 1 < args.Length)
            {
                return Map(args[i + 1]);
            }

            if (arg.StartsWith("--verbosity=", StringComparison.Ordinal))
            {
                return Map(arg["--verbosity=".Length..]);
            }
        }

        return LogLevel.Information;
    }

    private static LogLevel Map(string value) => value.ToLowerInvariant() switch
    {
        "q" or "quiet" => LogLevel.Warning,
        "m" or "minimal" => LogLevel.Warning,
        "n" or "normal" => LogLevel.Information,
        "d" or "detailed" => LogLevel.Debug,
        "diag" or "diagnostic" => LogLevel.Trace,
        _ => LogLevel.Information,
    };
}
