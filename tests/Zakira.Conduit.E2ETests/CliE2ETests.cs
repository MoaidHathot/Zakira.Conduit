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
                { "name": "alpha", "source": { "type": "github", "repo": "o/r" }, "targets": ["./out1"] },
                { "name": "beta",  "source": { "type": "github", "repo": "o/r", "branch": "main" }, "targets": ["./out2"] }
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
                  "source": { "type": "github", "repo": "acme/skills" },
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
                { "name": "d", "source": { "type": "github", "repo": "o/r" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("t"))}} ] }
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
                { "name": "x", "source": { "type": "github", "repo": "nope/nope" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("t"))}} ] }
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
        Directory.CreateDirectory(Path.Combine(xdg, "Zakira.Conduit"));
        var manifestPath = Path.Combine(xdg, "Zakira.Conduit", "conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                { "name": "demo", "source": { "type": "github", "repo": "acme/skills" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ] }
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

    [Fact]
    public async Task sync_github_paths_mirrors_each_subpath_into_targets()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-multi", new Dictionary<string, string>
        {
            ["skills/code-review/SKILL.md"] = "review",
            ["skills/test-writer/SKILL.md"] = "tests",
            ["skills/refactor/SKILL.md"] = "refactor",
            ["unrelated/IGNORE.md"] = "x",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "anthropic-bundle",
                  "source": {
                    "type": "github",
                    "repo": "https://github.com/acme/skills",
                    "paths": ["skills/code-review", "skills/test-writer", "skills/refactor"]
                  },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("agent"))}} ]
                }
              ]
            }
            """);

        var env = new Dictionary<string, string?>
        {
            ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString(),
        };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"sync should succeed. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        File.ReadAllText(tmp.Combine("agent", "code-review", "SKILL.md")).Should().Be("review");
        File.ReadAllText(tmp.Combine("agent", "test-writer", "SKILL.md")).Should().Be("tests");
        File.ReadAllText(tmp.Combine("agent", "refactor", "SKILL.md")).Should().Be("refactor");
        // Entry name does NOT appear in destinations under multi-path mode.
        Directory.Exists(tmp.Combine("agent", "anthropic-bundle")).Should().BeFalse();
        // And only the requested sub-paths were extracted.
        Directory.Exists(tmp.Combine("agent", "unrelated")).Should().BeFalse();
    }

    [Fact]
    public async Task sync_local_paths_mirrors_each_directory_under_its_basename()
    {
        using var tmp = new TempDir();

        Directory.CreateDirectory(tmp.Combine("source", "alpha"));
        await File.WriteAllTextAsync(tmp.Combine("source", "alpha", "SKILL.md"), "alpha");

        Directory.CreateDirectory(tmp.Combine("source", "beta"));
        await File.WriteAllTextAsync(tmp.Combine("source", "beta", "SKILL.md"), "beta");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "bundle",
                  "source": {
                    "type": "local",
                    "paths": ["./source/alpha", "./source/beta"]
                  },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("agent"))}} ]
                }
              ]
            }
            """);

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0, because: $"sync should succeed. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");
        File.ReadAllText(tmp.Combine("agent", "alpha", "SKILL.md")).Should().Be("alpha");
        File.ReadAllText(tmp.Combine("agent", "beta", "SKILL.md")).Should().Be("beta");
        Directory.Exists(tmp.Combine("agent", "bundle")).Should().BeFalse();
    }

    [Fact]
    public async Task sync_accepts_github_url_form_in_the_repo_field()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-url", new Dictionary<string, string>
        {
            ["SKILL.md"] = "via-url",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "demo",
                  "source": { "type": "github", "repo": "https://github.com/acme/skills.git" },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ]
                }
              ]
            }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");
        File.ReadAllText(tmp.Combine("dest", "demo", "SKILL.md")).Should().Be("via-url");
    }
}
