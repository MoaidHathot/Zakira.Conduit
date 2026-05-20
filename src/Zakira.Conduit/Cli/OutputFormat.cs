namespace Zakira.Conduit.Cli;

/// <summary>
///     CLI output format. Selected via the global <c>--output</c> option.
/// </summary>
internal enum OutputFormat
{
    /// <summary>Human-friendly text. Default.</summary>
    Text,

    /// <summary>Machine-readable JSON (pretty-printed) on stdout.</summary>
    Json,
}
