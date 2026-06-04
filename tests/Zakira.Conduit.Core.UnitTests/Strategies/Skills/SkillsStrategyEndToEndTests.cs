using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Strategies;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Core.UnitTests.Strategies.Skills;

/// <summary>
///     End-to-end tests for the skills strategy: drives the synchronizer
///     with a fetcher that produces SKILL.md-bearing folders, then inspects
///     the directory layout the harness scan + walker produces.
/// </summary>
public sealed class SkillsStrategyEndToEndTests
{
    private static (DefaultConduitSynchronizer sync, FakeFetcher fetcher) BuildSync()
    {
        var fetcher = new FakeFetcher();
        var registry = new DefaultSourceFetcherRegistry([fetcher]);
        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var env = new FakeEnvironment();
        var resolver = new DefaultPathResolver(env);
        var sync = new DefaultConduitSynchronizer(
            registry,
            mirror,
            resolver,
            new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance),
            StrategyTestHelper.BuildDefaultRegistry(resolver),
            NullLogger<DefaultConduitSynchronizer>.Instance);
        return (sync, fetcher);
    }

    [Fact]
    public async Task Skills_strategy_against_literal_skills_target_writes_each_skill_at_top_level()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["skills/alpha/SKILL.md"] = "---\nname: alpha\n---\n",
            ["skills/beta/SKILL.md"] = "---\nname: beta\n---\n",
            ["readme.md"] = "ignored",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var target = tmp.Combine("destination", "skills"); // trailing "skills" -> auto-detect
        Directory.CreateDirectory(target);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "container",
                    Strategy = "skills",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [target],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        File.Exists(Path.Combine(target, "alpha", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(target, "beta", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Skills_strategy_fans_out_across_every_detected_harness()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["alpha/SKILL.md"] = "---\nname: alpha\n---\n",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var home = tmp.Combine("home");
        Directory.CreateDirectory(Path.Combine(home, ".claude", "skills"));
        Directory.CreateDirectory(Path.Combine(home, ".opencode", "skills"));

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "container",
                    Strategy = "skills",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [home],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        File.Exists(Path.Combine(home, ".claude", "skills", "alpha", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(home, ".opencode", "skills", "alpha", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Explicit_skills_filter_restricts_which_are_mirrored()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["alpha/SKILL.md"] = "---\nname: alpha\n---\n",
            ["beta/SKILL.md"] = "---\nname: beta\n---\n",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var target = tmp.Combine("destination", "skills");
        Directory.CreateDirectory(target);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "container",
                    Strategy = "skills",
                    Skills = new[] { "alpha" },
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [target],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        Directory.Exists(Path.Combine(target, "alpha")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "beta")).Should().BeFalse();
    }

    [Fact]
    public async Task GroupBy_source_wraps_each_skill_in_a_source_named_subfolder()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["alpha/SKILL.md"] = "---\nname: alpha\n---\n",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var target = tmp.Combine("destination", "skills");
        Directory.CreateDirectory(target);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "container",
                    Strategy = "skills",
                    GroupBy = GroupBy.Source,
                    Source = new GitHubSource { Repo = "owner/cool-repo" },
                    Targets = [target],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        File.Exists(Path.Combine(target, "cool-repo", "alpha", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task No_SKILL_md_files_in_source_produces_a_clear_error()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["readme.md"] = "no skills here",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var target = tmp.Combine("destination", "skills");
        Directory.CreateDirectory(target);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "container",
                    Strategy = "skills",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [target],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeFalse();
        report.Entries[0].Error.Should().Contain("no SKILL.md files were found");
    }
}
