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

    [Fact]
    public async Task list_json_emits_valid_machine_readable_payload()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            {
              "version": 1,
              "entries": [
                { "name": "alpha", "source": { "type": "github", "repo": "o/r" }, "targets": ["./out"] },
                { "name": "beta",  "source": { "type": "local",  "path": "./skills" }, "targets": ["./out"] }
              ]
            }
            """);

        var result = await ConduitCli.RunAsync(["list", "--manifest", manifestPath, "--output", "json"]);

        result.ExitCode.Should().Be(0);
        using var doc = System.Text.Json.JsonDocument.Parse(result.StdOut);
        var root = doc.RootElement;
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("manifest").GetString().Should().NotBeNullOrEmpty();
        var entries = root.GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);
        entries[0].GetProperty("name").GetString().Should().Be("alpha");
        entries[0].GetProperty("source").GetProperty("type").GetString().Should().Be("github");
        entries[1].GetProperty("source").GetProperty("type").GetString().Should().Be("local");
    }

    [Fact]
    public async Task sync_json_emits_succeeded_dryrun_and_per_entry_payload()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-json", new Dictionary<string, string>
        {
            ["SKILL.md"] = "ok",
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
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ]
                }
              ]
            }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };

        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath, "--output", "json", "--dry-run"], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        // stderr can contain log noise; stdout must be a single, clean JSON document.
        using var doc = System.Text.Json.JsonDocument.Parse(result.StdOut);
        var root = doc.RootElement;
        root.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        root.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        root.GetProperty("exitCode").GetInt32().Should().Be(0);
        root.GetProperty("entries").GetArrayLength().Should().Be(1);
        root.GetProperty("entries")[0].GetProperty("name").GetString().Should().Be("demo");
    }

    [Fact]
    public async Task validate_json_emits_ok_payload_on_success()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            { "version": 1, "entries": [ { "name": "x", "source": { "type": "github", "repo": "o/r" }, "targets": ["./out"] } ] }
            """);

        var result = await ConduitCli.RunAsync(["validate", "--manifest", manifestPath, "--output", "json"]);

        result.ExitCode.Should().Be(0);
        using var doc = System.Text.Json.JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("manifest").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task validate_json_emits_error_payload_on_failure()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            { "version": 1, "entries": [ { "name": "", "source": { "type": "github", "repo": "owner/repo" }, "targets": [] } ] }
            """);

        var result = await ConduitCli.RunAsync(["validate", "--manifest", manifestPath, "--output", "json"]);

        result.ExitCode.Should().Be(2);
        using var doc = System.Text.Json.JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        var details = doc.RootElement.GetProperty("details");
        details.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task init_interactive_refuses_to_run_with_redirected_streams()
    {
        // The E2E harness always pipes stdin/stdout, so --interactive should
        // bail out fast with exit code 2 rather than hang waiting for input.
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");

        var result = await ConduitCli.RunAsync(["init", "--manifest", manifestPath, "--interactive"]);

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--interactive");
        File.Exists(manifestPath).Should().BeFalse();
    }

    [Fact]
    public async Task sync_logs_are_emitted_on_stderr_not_stdout_when_json()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("o-r-xx", new Dictionary<string, string> { ["a.txt"] = "x" });
        server.MapZipball("o", "r", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            { "version": 1, "entries": [ { "name": "x", "source": { "type": "github", "repo": "o/r" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("t"))}} ] } ] }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        var result = await ConduitCli.RunAsync(["sync", "--manifest", manifestPath, "--output", "json"], environmentOverrides: env);

        // stdout should be parseable as a single JSON object - no logger noise mixed in.
        var trimmed = result.StdOut.Trim();
        trimmed.Should().StartWith("{");
        trimmed.Should().EndWith("}");
        Action parseStdout = () => System.Text.Json.JsonDocument.Parse(trimmed);
        parseStdout.Should().NotThrow("stdout must remain a pure JSON document with logs routed to stderr");
    }

    [Fact]
    public async Task pin_resolves_branch_and_writes_commit_into_manifest()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        const string sha = "abcdef0123456789abcdef0123456789abcdef01";

        // Mock the commits endpoint used by IGitHubRefResolver.
        server.Map("/repos/acme/skills/commits/main", async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = System.Text.Encoding.UTF8.GetBytes($"{{\"sha\":\"{sha}\"}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });

        var manifestPath = tmp.Combine("conduit.json");
        const string originalManifest = """
            {
              "version": 1,
              "entries": [
                {
                  "name": "demo",
                  "description": "An entry that should keep its description",
                  "source": { "type": "github", "repo": "acme/skills", "branch": "main" },
                  "targets": ["./out"]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(manifestPath, originalManifest);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        var result = await ConduitCli.RunAsync(["pin", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(0, because: $"stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        var written = await File.ReadAllTextAsync(manifestPath);
        using var doc = System.Text.Json.JsonDocument.Parse(written);
        var entry = doc.RootElement.GetProperty("entries")[0];
        entry.GetProperty("source").GetProperty("commit").GetString().Should().Be(sha);
        // The branch should be retained as the tracking intent.
        entry.GetProperty("source").GetProperty("branch").GetString().Should().Be("main");
        // Other top-level fields on the entry must survive the rewrite.
        entry.GetProperty("description").GetString().Should().Be("An entry that should keep its description");

        // A backup of the original manifest must have been written.
        var backupPath = manifestPath + ".bak";
        File.Exists(backupPath).Should().BeTrue();
        (await File.ReadAllTextAsync(backupPath)).Should().Be(originalManifest);
    }

    [Fact]
    public async Task pin_dry_run_does_not_rewrite_the_manifest()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        const string sha = "abcdef0123456789abcdef0123456789abcdef01";
        server.Map("/repos/acme/skills/commits/main", async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = System.Text.Encoding.UTF8.GetBytes($"{{\"sha\":\"{sha}\"}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });

        var manifestPath = tmp.Combine("conduit.json");
        const string original = """
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills", "branch": "main" }, "targets": ["./out"] } ] }
            """;
        await File.WriteAllTextAsync(manifestPath, original);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        var result = await ConduitCli.RunAsync(["pin", "--manifest", manifestPath, "--dry-run"], environmentOverrides: env);

        result.ExitCode.Should().Be(0);

        var after = await File.ReadAllTextAsync(manifestPath);
        after.Should().Be(original, because: "--dry-run must not modify the manifest");
    }

    [Fact]
    public async Task update_alias_works_the_same_as_pin()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        const string sha = "abcdef0123456789abcdef0123456789abcdef01";
        server.Map("/repos/acme/skills/commits/main", async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = System.Text.Encoding.UTF8.GetBytes($"{{\"sha\":\"{sha}\"}}");
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            ctx.Response.Close();
        });

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills", "branch": "main", "commit": "0000000000000000000000000000000000000000" }, "targets": ["./out"] } ] }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        var result = await ConduitCli.RunAsync(["update", "--manifest", manifestPath], environmentOverrides: env);

        result.ExitCode.Should().Be(0);

        var written = await File.ReadAllTextAsync(manifestPath);
        using var doc = System.Text.Json.JsonDocument.Parse(written);
        doc.RootElement.GetProperty("entries")[0].GetProperty("source").GetProperty("commit").GetString().Should().Be(sha);
    }

    [Fact]
    public async Task pin_skips_entries_without_a_branch_with_a_clear_message()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills", "commit": "0000000000000000000000000000000000000000" }, "targets": ["./out"] } ] }
            """);

        var result = await ConduitCli.RunAsync(["pin", "--manifest", manifestPath]);
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("no 'branch' field");
    }

    [Fact]
    public async Task watch_runs_initial_sync_then_resyncs_when_manifest_changes()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-watch", new Dictionary<string, string>
        {
            ["SKILL.md"] = "watch-content",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ] } ] }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };

        // Spawn watch in the background. Cancel after a short wait by cancelling
        // the harness CancellationToken (which kills the process).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var watchTask = ConduitCli.RunAsync(
            ["watch", "--manifest", manifestPath, "--debounce", "100"],
            environmentOverrides: env,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cts.Token);

        // Wait for the initial sync to land. Allow generously for cold-start
        // JIT on slow CI runners (Ubuntu can take a few seconds the first time).
        var initialPath = tmp.Combine("dest", "demo", "SKILL.md");
        await WaitForFileAsync(initialPath, TimeSpan.FromSeconds(15));
        File.ReadAllText(initialPath).Should().Be("watch-content");

        // Give the in-process FileSystemWatcher a beat to settle after the
        // initial sync completes - on Linux there's a tiny inotify-arming
        // gap that an immediate manifest write could race past, and the
        // production fix is to set up the watcher BEFORE the initial sync
        // (see WatchCommandHandler).
        await Task.Delay(500);

        // Modify the manifest to trigger a re-sync. Switch to a different repo
        // (mock returns different content, so we can prove the new sync ran).
        var payload2 = ZipballPayload.Build("acme-skills-watch-v2", new Dictionary<string, string>
        {
            ["SKILL.md"] = "watch-content-v2",
        });
        server.MapZipball("acme", "skills2", gitRef: null, payload2);

        await File.WriteAllTextAsync(manifestPath, $$"""
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills2" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ] } ] }
            """);

        await WaitForContentAsync(initialPath, "watch-content-v2", TimeSpan.FromSeconds(15));
        File.ReadAllText(initialPath).Should().Be("watch-content-v2");

        // Stop the watch process. We expect cancellation; an exit-code check
        // is not meaningful when the OS terminated the child.
        cts.Cancel();
        try { await watchTask; } catch (TimeoutException) { /* expected when we killed it */ }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) { return; }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Timed out waiting for '{path}' to appear.");
    }

    private static async Task WaitForContentAsync(string path, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == expected) { return; }
            }
            catch (IOException) { /* mid-write; retry */ }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Timed out waiting for '{path}' to contain '{expected}'.");
    }

    [Fact]
    public async Task status_text_reports_never_synced_when_state_is_missing()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, """
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "local", "path": "./skills" }, "targets": ["./out"] } ] }
            """);

        var result = await ConduitCli.RunAsync(["status", "--manifest", manifestPath]);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("demo");
        result.StdOut.Should().Contain("never synced");
        result.StdOut.Should().Contain("missing");
    }

    [Fact]
    public async Task status_json_reflects_a_post_sync_state_file()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("acme-skills-statusjson", new Dictionary<string, string>
        {
            ["SKILL.md"] = "hi",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "version": 1,
              "entries": [
                {
                  "name": "demo",
                  "source": { "type": "github", "repo": "acme/skills", "commit": "0000000000000000000000000000000000000000" },
                  "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ]
                }
              ]
            }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        (await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env)).ExitCode.Should().Be(0);

        var statusResult = await ConduitCli.RunAsync(["status", "--manifest", manifestPath, "--output", "json"]);
        statusResult.ExitCode.Should().Be(0);

        using var doc = System.Text.Json.JsonDocument.Parse(statusResult.StdOut);
        var root = doc.RootElement;
        root.GetProperty("stateFilePresent").GetBoolean().Should().BeTrue();
        var entries = root.GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);
        var demo = entries[0];
        demo.GetProperty("name").GetString().Should().Be("demo");
        demo.GetProperty("neverSynced").GetBoolean().Should().BeFalse();
        demo.GetProperty("allTargetsPresent").GetBoolean().Should().BeTrue();
        demo.GetProperty("resolvedRef").GetString().Should().Be("0000000000000000000000000000000000000000");
    }

    [Fact]
    public async Task status_text_flags_targets_drifted_when_a_target_was_deleted()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();
        var payload = ZipballPayload.Build("acme-skills-drift", new Dictionary<string, string>
        {
            ["SKILL.md"] = "hi",
        });
        server.MapZipball("acme", "skills", gitRef: null, payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            { "version": 1, "entries": [ { "name": "demo", "source": { "type": "github", "repo": "acme/skills", "commit": "abc1234567890abcdef1234567890abcdef12345" }, "targets": [ {{System.Text.Json.JsonSerializer.Serialize(tmp.Combine("dest"))}} ] } ] }
            """);

        var env = new Dictionary<string, string?> { ["CONDUIT_GITHUB_API_BASE"] = server.BaseAddress.ToString() };
        (await ConduitCli.RunAsync(["sync", "--manifest", manifestPath], environmentOverrides: env)).ExitCode.Should().Be(0);

        // Delete the mirrored target to simulate user drift.
        Directory.Delete(tmp.Combine("dest", "demo"), recursive: true);

        var statusResult = await ConduitCli.RunAsync(["status", "--manifest", manifestPath]);
        statusResult.ExitCode.Should().Be(0);
        statusResult.StdOut.Should().Contain("targets drifted");
    }
}
