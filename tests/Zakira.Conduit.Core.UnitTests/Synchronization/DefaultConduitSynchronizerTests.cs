using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Core.UnitTests.Synchronization;

public sealed class DefaultConduitSynchronizerTests
{
    private static (DefaultConduitSynchronizer synchronizer, FakeFetcher fetcher, FakeEnvironment env) Build()
    {
        var fetcher = new FakeFetcher();
        var registry = new DefaultSkillSourceFetcherRegistry([fetcher]);
        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var env = new FakeEnvironment();
        var resolver = new DefaultPathResolver(env);
        var synchronizer = new DefaultConduitSynchronizer(registry, mirror, resolver, NullLogger<DefaultConduitSynchronizer>.Instance);
        return (synchronizer, fetcher, env);
    }

    [Fact]
    public async Task Mirrors_each_entry_into_a_subdir_named_after_the_entry()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["SKILL.md"] = "hello world",
            ["data/x.json"] = "{}",
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
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },
                    Targets = [tmp.Combine("targetA"), tmp.Combine("targetB")],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Succeeded.Should().BeTrue();
        report.Entries.Should().ContainSingle().Which.Targets.Should().HaveCount(2);

        File.ReadAllText(tmp.Combine("targetA", "alpha", "SKILL.md")).Should().Be("hello world");
        File.ReadAllText(tmp.Combine("targetB", "alpha", "data", "x.json")).Should().Be("{}");
    }

    [Fact]
    public async Task DryRun_does_not_write_to_targets()
    {
        using var tmp = new TempDir();
        var (sync, _, _) = Build();

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "alpha",
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },
                    Targets = [tmp.Combine("target")],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { DryRun = true });

        report.Succeeded.Should().BeTrue();
        report.DryRun.Should().BeTrue();
        Directory.Exists(tmp.Combine("target", "alpha")).Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_entries_are_skipped()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "skipme",
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },
                    Targets = [tmp.Combine("t")],
                    Disabled = true,
                }
            ],
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Entries[0].Skipped.Should().BeTrue();
        fetcher.FetchCount.Should().Be(0);
    }

    [Fact]
    public async Task EntryNames_filter_restricts_what_is_synced()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry { Name = "alpha", Source = new GitHubSkillSource { Owner = "o", Repo = "r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "beta",  Source = new GitHubSkillSource { Owner = "o", Repo = "r" }, Targets = [tmp.Combine("t")] },
            ],
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { EntryNames = ["beta"] });

        fetcher.FetchCount.Should().Be(1);
        report.Entries.Single(e => e.Entry.Name == "alpha").Skipped.Should().BeTrue();
        report.Entries.Single(e => e.Entry.Name == "beta").Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Failure_in_one_entry_does_not_block_the_next_by_default()
    {
        using var tmp = new TempDir();
        var fetcher = new ThrowingFetcher();
        var registry = new DefaultSkillSourceFetcherRegistry([fetcher]);
        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var env = new FakeEnvironment();
        var resolver = new DefaultPathResolver(env);
        var sync = new DefaultConduitSynchronizer(registry, mirror, resolver, NullLogger<DefaultConduitSynchronizer>.Instance);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry { Name = "fails", Source = new GitHubSkillSource { Owner = "boom", Repo = "r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "ok",    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },    Targets = [tmp.Combine("t")] },
            ],
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Entries.Should().HaveCount(2);
        report.Entries[0].Succeeded.Should().BeFalse();
        report.Entries[1].Succeeded.Should().BeTrue();
        report.Succeeded.Should().BeFalse();
        report.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task StopOnFirstError_aborts_remaining_entries()
    {
        using var tmp = new TempDir();
        var fetcher = new ThrowingFetcher();
        var registry = new DefaultSkillSourceFetcherRegistry([fetcher]);
        var sync = new DefaultConduitSynchronizer(
            registry,
            new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance),
            new DefaultPathResolver(new FakeEnvironment()),
            NullLogger<DefaultConduitSynchronizer>.Instance);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry { Name = "fails", Source = new GitHubSkillSource { Owner = "boom", Repo = "r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "ok",    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },    Targets = [tmp.Combine("t")] },
            ],
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { StopOnFirstError = true });

        report.Entries.Should().HaveCount(1);
        report.Entries[0].Entry.Name.Should().Be("fails");
    }

    [Fact]
    public async Task Overlap_between_source_and_target_is_reported_as_an_error()
    {
        using var tmp = new TempDir();
        var sourceDir = tmp.Combine("src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "x");

        var fetcher = new FakeFetcher();
        fetcher.ContentProvider = _ => new Dictionary<string, string> { ["SKILL.md"] = "x" };

        // Force the fake fetcher to "fetch" by pointing the resulting content
        // at the same directory the user is trying to target.
        var registry = new DefaultSkillSourceFetcherRegistry([new InPlaceFetcher(sourceDir)]);
        var sync = new DefaultConduitSynchronizer(
            registry,
            new AtomicDirectoryMirror(Microsoft.Extensions.Logging.Abstractions.NullLogger<AtomicDirectoryMirror>.Instance),
            new DefaultPathResolver(new FakeEnvironment()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultConduitSynchronizer>.Instance);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    // Target = parent of source, with entry name = "src" -> target+name == source dir.
                    Name = "src",
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r" },
                    Targets = [tmp.Path],
                }
            ],
        };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Succeeded.Should().BeFalse();
        report.Entries[0].Targets[0].Error.Should().Contain("overlap");
    }

    /// <summary>
    ///     A fetcher that ignores the source and returns a fixed local directory.
    ///     Used to set up the overlap-guard test without going through the GitHub fetcher.
    /// </summary>
    private sealed class InPlaceFetcher(string directory) : ISkillSourceFetcher
    {
        public string SourceKind => "github";

        public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FetchedSource(directory, source, resolvedRef: null, cleanup: null));
    }

    private sealed class ThrowingFetcher : ISkillSourceFetcher
    {
        public string SourceKind => "github";

        public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default)
        {
            if (source is GitHubSkillSource gh && gh.Owner == "boom")
            {
                throw new InvalidOperationException("boom");
            }

            var dir = Path.Combine(Path.GetTempPath(), "conduit-throwfetcher", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "ok");
            return Task.FromResult(new FetchedSource(dir, source, resolvedRef: null, cleanup: () =>
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
                return ValueTask.CompletedTask;
            }));
        }
    }
}
