using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Strategies;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Core.UnitTests.Strategies;

/// <summary>
///     End-to-end-ish tests for each Phase 1 strategy: drive the synchronizer
///     with a FakeFetcher that lays out a controlled file tree, then inspect
///     the resulting target directory.
/// </summary>
public sealed class StrategyEndToEndTests
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
    public async Task Wrap_strategy_remains_the_default_when_strategy_is_null()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["SKILL.md"] = "wrap",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "alpha",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [tmp.Combine("target")],
                    // Strategy not set -> wrap
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();
        File.ReadAllText(tmp.Combine("target", "alpha", "SKILL.md")).Should().Be("wrap");
    }

    [Fact]
    public async Task Flat_strategy_writes_contents_directly_into_target()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["SKILL.md"] = "flat",
            ["data/file.txt"] = "x",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "flatentry",
                    Strategy = "flat",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        // No 'flatentry' wrapping dir.
        File.ReadAllText(tmp.Combine("dest", "SKILL.md")).Should().Be("flat");
        File.ReadAllText(tmp.Combine("dest", "data", "file.txt")).Should().Be("x");
        Directory.Exists(tmp.Combine("dest", "flatentry")).Should().BeFalse();
    }

    [Fact]
    public async Task Flat_strategy_with_groupBy_source_wraps_in_source_name()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["file.txt"] = "x",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "named-by-user",
                    Strategy = "flat",
                    GroupBy = GroupBy.Source,
                    Source = new GitHubSource { Repo = "owner/myrepo" },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        // groupBy: source wraps in the source-derived name (the repo name),
        // not the entry name.
        File.ReadAllText(tmp.Combine("dest", "myrepo", "file.txt")).Should().Be("x");
    }

    [Fact]
    public async Task Expand_strategy_emits_one_subdir_per_top_level_child()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["README.md"] = "ignored at root",
            ["alpha/SKILL.md"] = "a",
            ["beta/SKILL.md"] = "b",
            ["beta/extra/inner.txt"] = "deep",
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "exp",
                    Strategy = "expand",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());
        report.Succeeded.Should().BeTrue();

        File.ReadAllText(tmp.Combine("dest", "alpha", "SKILL.md")).Should().Be("a");
        File.ReadAllText(tmp.Combine("dest", "beta", "SKILL.md")).Should().Be("b");
        File.ReadAllText(tmp.Combine("dest", "beta", "extra", "inner.txt")).Should().Be("deep");

        // The README at the source root is silently skipped (no per-child dest).
        File.Exists(tmp.Combine("dest", "README.md")).Should().BeFalse();
    }

    [Fact]
    public async Task Flat_strategy_rejects_per_target_alias_at_validation()
    {
        using var tmp = new TempDir();
        var (sync, fetcher) = BuildSync();
        fetcher.ContentProvider = _ => new Dictionary<string, string> { ["x"] = "y" };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var registry = StrategyTestHelper.BuildDefaultRegistry();

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "bad",
                    Strategy = "flat",
                    Source = new GitHubSource { Repo = "o/r" },
                    Targets = [new PathSpec(tmp.Combine("dest"), "aliased")],
                }
            ],
        };

        var errors = ManifestValidator.Validate(manifest, registry);
        errors.Should().Contain(e => e.Contains("'as' aliases are not supported by strategy 'flat'"));
    }
}
