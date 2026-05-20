using Zakira.Conduit.E2ETests.TestHelpers;

namespace Zakira.Conduit.E2ETests;

/// <summary>
///     Makes sure the runnable sample manifest in <c>example/conduit.json</c>
///     stays in sync with the CLI: parses, validates, and lists cleanly. If
///     this test ever fails the example has drifted from the schema/CLI and
///     should be regenerated.
/// </summary>
public sealed class SampleManifestTests
{
    private static string LocateExampleManifest()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(SampleManifestTests).Assembly.Location)
                          ?? throw new InvalidOperationException("Cannot locate test assembly directory.");

        var dir = new DirectoryInfo(assemblyDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "example", "conduit.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not find example/conduit.json by walking up from the test assembly directory.");
    }

    [Fact]
    public async Task example_manifest_validates()
    {
        var manifestPath = LocateExampleManifest();

        var result = await ConduitCli.RunAsync(["validate", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0, because: $"stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");
        result.StdOut.Should().Contain("OK");
    }

    [Fact]
    public async Task example_manifest_lists_every_entry()
    {
        var manifestPath = LocateExampleManifest();

        var result = await ConduitCli.RunAsync(["list", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("PowerReview");
        result.StdOut.Should().Contain("internal-runbooks");
        result.StdOut.Should().Contain("local-skill-sample");
        result.StdOut.Should().Contain("local-skill-bundle");
        result.StdOut.Should().Contain("experimental-thing");
        result.StdOut.Should().Contain("zakira-conduit");
        result.StdOut.Should().Contain("(disabled)");
        result.StdOut.Should().Contain("github:MoaidHathot/PowerReview");
        result.StdOut.Should().Contain("local:./local-skill-sample");
        result.StdOut.Should().Contain("local:../skills/zakira-conduit");
    }
}
