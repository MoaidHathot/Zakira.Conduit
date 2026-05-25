namespace Zakira.Conduit.Sources.Azdo.Credentials;

/// <summary>
///     Abstraction over starting an external process and capturing its output.
///     Exists so the <c>az</c> CLI credential provider can be unit-tested
///     without spawning a real process.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    ///     Runs <paramref name="fileName"/> with the supplied arguments and
    ///     returns the exit code, captured stdout, and captured stderr.
    /// </summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>The captured outcome of running an external process.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
