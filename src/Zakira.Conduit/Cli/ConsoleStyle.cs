namespace Zakira.Conduit.Cli;

/// <summary>
///     Tiny ANSI colour helper. When the process detects it's not writing to a
///     TTY (output is redirected to a pipe / file) or the standard
///     <a href="https://no-color.org/">NO_COLOR</a> environment variable is set,
///     all helpers return the input unchanged.
/// </summary>
internal sealed class ConsoleStyle
{
    private const string Reset = "\u001b[0m";

    /// <summary>True when ANSI escape sequences are emitted.</summary>
    public bool ColorEnabled { get; }

    public ConsoleStyle(bool colorEnabled)
    {
        ColorEnabled = colorEnabled;
    }

    /// <summary>
    ///     Inspects the host environment and returns a style configured for it:
    ///     colour on when stdout is a real terminal and <c>NO_COLOR</c> is unset.
    /// </summary>
    public static ConsoleStyle DetectFromEnvironment()
    {
        if (Console.IsOutputRedirected)
        {
            return new ConsoleStyle(colorEnabled: false);
        }

        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        if (!string.IsNullOrEmpty(noColor))
        {
            return new ConsoleStyle(colorEnabled: false);
        }

        return new ConsoleStyle(colorEnabled: true);
    }

    public string Red(string text)    => Wrap(text, "\u001b[31m");
    public string Green(string text)  => Wrap(text, "\u001b[32m");
    public string Yellow(string text) => Wrap(text, "\u001b[33m");
    public string Cyan(string text)   => Wrap(text, "\u001b[36m");
    public string Dim(string text)    => Wrap(text, "\u001b[2m");
    public string Bold(string text)   => Wrap(text, "\u001b[1m");

    private string Wrap(string text, string ansiPrefix) =>
        ColorEnabled ? ansiPrefix + text + Reset : text;
}
