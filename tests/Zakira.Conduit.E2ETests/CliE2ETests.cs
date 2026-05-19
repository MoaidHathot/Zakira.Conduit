using Zakira.Conduit.E2ETests.TestHelpers;
using Zakira.Conduit.IntegrationTests.TestHelpers;

namespace Zakira.Conduit.E2ETests;

/// <summary>
///     End-to-end tests that spawn the published <c>conduit</c> CLI as a
///     subprocess and exercise its commands against an in-process mock of the
///     GitHub API.
/// </summary>
public sealed class CliE2ETests
{
    [Fact]
    public async Task help_prints_usage_and_exits_zero()
    {
        var result = await ConduitCli.RunAsync(["--help"]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("conduit");
        result.StdOut.Should().Contain("sync");
        result.StdOut.Should().Contain("validate");
    }

    [Fact]
    public async Task version_prints_a_semver_like_string()
    {
        var result = await ConduitCli.RunAsync(["--version"]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task init_creates_a_valid_manifest_that_validate_accepts()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");

        var initResult = await ConduitCli.RunAsync(["init", "--manifest", manifestPath]);
        initResult.ExitCode.Should().Be(0);
        File.Exists(manifestPath).Should().BeTrue();

        var validateResult = await ConduitCli.RunAsync(["validate", "--manifest", manifestPath]);
        validateResult.ExitCode.Should().Be(0);
        validateResult.StdOut.Should().Contain("OK");
    }

    [Fact]
    public async Task init_refuses_to_overwrite_without_force()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var result = await ConduitCli.RunAsync(["init", "--manifest", manifestPath]);
        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("already exists");
    }

    [Fact]
    public async Task list_prints_each_manifest_entry()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            {
              "version": 1,
              "entries": [
                { "name": "alpha", "source": { "type": "github", "owner": "o", "repo": "r" }, "targets": ["./out1"] },
                { "name": "beta",  "source": { "type": "github", "owner": "o", "repo": "r", "branch": "main" }, "targets": ["./out2"] }
              ]
            }
            """);

        var result = await ConduitCli.RunAsync(["list", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("alpha");
        result.StdOut.Should().Contain("beta");
        result.StdOut.Should().Contain("github:o/r");
    }

    [Fact]
    public async Task sync_uses_explicit_manifest_and_mirrors_into_each_target()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-abc123", new Dictionary<string, string>
        {
            ["SKILL.md"] = "hello from e2e",
            ["data.json"] = "{}",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "demo",
                  "source": { "type": "github", "owner": "acme", "repo": "skills" },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("a"))}}, {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("b"))}} ]
                }
              ]
            }
            """);

        var env = new Dictionary<string, string?>
        {
            ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString(),
            ["CONDUIT_GITHUB_TOKEN"] = null,
            ["GITHUB_TOKEN"] = null,
        };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"sync should succeed. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");
        File.ReadAllText(tmp.Combine("a", "demo", "SKILL.md")).Should().Be("hello from e2e");
        File.ReadAllText(tmp.Combine("b", "demo", "data.json")).Should().Be("{}");
    }

    [Fact]
    public async Task sync_dry_run_does_not_write_anything()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("o-r-xx", new Dictionary<string, string> { ["a.txt"] = "x" });
        server.MapZipball("o", "r", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                { "name": "d", "source": { "type": "github", "owner": "o", "repo": "r" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("t"))}} ] }
              ]
            }
            """);

        var env = new Dictionary<string, string?>
        {
            ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString(),
        };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath, "--dry-run"], environmentOverrides: env);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("dry-run");
        Directory.Exists(tmp.Combine("t", "d")).Should().BeFalse();
    }

    [Fact]
    public async Task sync_reports_failure_with_nonzero_exit_code_when_github_returns_404()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        server.MapZipball("nope", "nope", gitRef: null, payload: [], status: System.Net.HttpStatusCode.NotFound);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                { "name": "x", "source": { "type": "github", "owner": "nope", "repo": "nope" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("t"))}} ] }
              ]
            }
            """);

        var env = new Dictionary<string, string?>
        {
            ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString(),
        };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(1);
        result.StdOut.Should().Contain("failed");
    }

    [Fact]
    public async Task xdg_config_home_discovers_the_manifest_when_no_flag_given()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-xdg", new Dictionary<string, string>
        {
            ["SKILL.md"] = "via xdg",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var xdg = tmp.Combine("xdg");
        Directory.CreateDirectory(Path.Combine(xdg, "conduit"));
        var manifestPath = Path.Combine(xdg, "conduit", "conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                { "name": "demo", "source": { "type": "github", "owner": "acme", "repo": "skills" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ] }
              ]
            }
            """);

        var env = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = xdg,
            ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString(),
        };

        var result = await ConduitCli.RunAsync(["sync"], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");
        File.ReadAllText(tmp.Combine("dest", "demo", "SKILL.md")).Should().Be("via xdg");
    }

    [Fact]
    public async Task sync_local_source_mirrors_a_directory_into_each_target()
    {
        using var tmp = new TempDir();

        // Lay out a "skill" folder on disk to use as a source.
        var sourceDir = tmp.Combine("source-skills", "my-skill");
        Directory.CreateDirectory(Path.Combine(sourceDir, "data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "from-local");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data", "rules.txt"), "rule-1");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "my-skill",
                  "source": { "type": "local", "path": "./source-skills/my-skill" },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("agentA"))}}, {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("agentB"))}} ]
                }
              ]
            }
            """);

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0, because: $"sync should succeed. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        File.ReadAllText(tmp.Combine("agentA", "my-skill", "SKILL.md")).Should().Be("from-local");
        File.ReadAllText(tmp.Combine("agentA", "my-skill", "data", "rules.txt")).Should().Be("rule-1");
        File.ReadAllText(tmp.Combine("agentB", "my-skill", "SKILL.md")).Should().Be("from-local");

        // The original source must not have been touched.
        File.ReadAllText(Path.Combine(sourceDir, "SKILL.md")).Should().Be("from-local");
    }

    [Fact]
    public async Task list_summarizes_local_source_with_a_path_marker()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            {
              "version": 1,
              "entries": [
                { "name": "a", "source": { "type": "local", "path": "./vendor/skills/a" }, "targets": ["./out"] }
              ]
            }
            """);

        var result = await ConduitCli.RunAsync(["list", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("local:./vendor/skills/a");
    }
}
