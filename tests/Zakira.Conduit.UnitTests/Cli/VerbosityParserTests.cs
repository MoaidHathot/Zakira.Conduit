using Microsoft.Extensions.Logging;
using Zakira.Conduit.Cli;

namespace Zakira.Conduit.UnitTests.Cli;

/// <summary>
///     Locks the verbosity ladder. Default floor is <see cref="LogLevel.Warning"/>
///     (matching <c>dotnet</c>'s <c>minimal</c>), <c>-q</c> drops below that to
///     <see cref="LogLevel.Error"/>, and <c>--verbosity normal</c> restores the
///     per-entry operational narration at <see cref="LogLevel.Information"/>.
/// </summary>
public sealed class VerbosityParserTests
{
    [Fact]
    public void No_flag_defaults_to_warning()
    {
        VerbosityParser.ParseFromArgs([]).Should().Be(LogLevel.Warning);
        VerbosityParser.ParseFromArgs(["sync"]).Should().Be(LogLevel.Warning);
        VerbosityParser.ParseFromArgs(["sync", "--manifest", "x.json"]).Should().Be(LogLevel.Warning);
    }

    [Theory]
    [InlineData("-q")]
    [InlineData("--quiet")]
    public void Quiet_shortcut_maps_to_error(string flag)
    {
        VerbosityParser.ParseFromArgs([flag]).Should().Be(LogLevel.Error);
        VerbosityParser.ParseFromArgs(["sync", flag]).Should().Be(LogLevel.Error);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--verbose")]
    public void Verbose_shortcut_maps_to_debug(string flag)
    {
        VerbosityParser.ParseFromArgs([flag]).Should().Be(LogLevel.Debug);
        VerbosityParser.ParseFromArgs(["sync", flag]).Should().Be(LogLevel.Debug);
    }

    [Theory]
    [InlineData("q", LogLevel.Error)]
    [InlineData("quiet", LogLevel.Error)]
    [InlineData("QUIET", LogLevel.Error)]
    [InlineData("m", LogLevel.Warning)]
    [InlineData("minimal", LogLevel.Warning)]
    [InlineData("Minimal", LogLevel.Warning)]
    [InlineData("n", LogLevel.Information)]
    [InlineData("normal", LogLevel.Information)]
    [InlineData("d", LogLevel.Debug)]
    [InlineData("detailed", LogLevel.Debug)]
    [InlineData("diag", LogLevel.Trace)]
    [InlineData("diagnostic", LogLevel.Trace)]
    [InlineData("DIAGNOSTIC", LogLevel.Trace)]
    public void Verbosity_two_token_form_maps_each_label(string label, LogLevel expected)
    {
        VerbosityParser.ParseFromArgs(["--verbosity", label]).Should().Be(expected);
        VerbosityParser.ParseFromArgs(["sync", "--verbosity", label]).Should().Be(expected);
    }

    [Theory]
    [InlineData("--verbosity=q", LogLevel.Error)]
    [InlineData("--verbosity=quiet", LogLevel.Error)]
    [InlineData("--verbosity=minimal", LogLevel.Warning)]
    [InlineData("--verbosity=normal", LogLevel.Information)]
    [InlineData("--verbosity=detailed", LogLevel.Debug)]
    [InlineData("--verbosity=diagnostic", LogLevel.Trace)]
    public void Verbosity_equals_form_maps_each_label(string token, LogLevel expected)
    {
        VerbosityParser.ParseFromArgs([token]).Should().Be(expected);
        VerbosityParser.ParseFromArgs(["sync", token]).Should().Be(expected);
    }

    [Fact]
    public void Unknown_verbosity_value_falls_back_to_default_floor()
    {
        // System.CommandLine will report this as a parse error in the normal
        // pipeline; the pre-parser only needs to pick a sane log floor in the
        // meantime. We pick the default floor (Warning) so an unrecognised
        // value behaves the same as "no flag".
        VerbosityParser.ParseFromArgs(["--verbosity", "bogus"]).Should().Be(LogLevel.Warning);
        VerbosityParser.ParseFromArgs(["--verbosity=bogus"]).Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Verbosity_without_value_is_ignored()
    {
        // Trailing "--verbosity" with no value would be a parse error in
        // System.CommandLine; the pre-parser silently skips it and keeps the
        // default floor.
        VerbosityParser.ParseFromArgs(["--verbosity"]).Should().Be(LogLevel.Warning);
        VerbosityParser.ParseFromArgs(["sync", "--verbosity"]).Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void First_recognised_flag_wins()
    {
        // The parser short-circuits on the first match it finds, scanning
        // left-to-right. This pins that behaviour so two conflicting flags
        // don't silently swap meaning depending on argument order.
        VerbosityParser.ParseFromArgs(["-q", "-v"]).Should().Be(LogLevel.Error);
        VerbosityParser.ParseFromArgs(["-v", "-q"]).Should().Be(LogLevel.Debug);
        VerbosityParser.ParseFromArgs(["--verbosity", "normal", "-q"]).Should().Be(LogLevel.Information);
    }
}
