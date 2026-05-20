using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.DependencyInjection;
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
        var synchronizer = new DefaultConduitSynchronizer(registry, mirror, resolver, new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance), NullLogger<DefaultConduitSynchronizer>.Instance);
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
                    Source = new GitHubSkillSource { Repo = "o/r" },
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
                    Source = new GitHubSkillSource { Repo = "o/r" },
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
                    Source = new GitHubSkillSource { Repo = "o/r" },
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
                new ConduitEntry { Name = "alpha", Source = new GitHubSkillSource { Repo = "o/r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "beta",  Source = new GitHubSkillSource { Repo = "o/r" }, Targets = [tmp.Combine("t")] },
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
        var sync = new DefaultConduitSynchronizer(registry, mirror, resolver, new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance), NullLogger<DefaultConduitSynchronizer>.Instance);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry { Name = "fails", Source = new GitHubSkillSource { Repo = "boom/r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "ok",    Source = new GitHubSkillSource { Repo = "o/r" },    Targets = [tmp.Combine("t")] },
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
            new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance),
            NullLogger<DefaultConduitSynchronizer>.Instance);

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry { Name = "fails", Source = new GitHubSkillSource { Repo = "boom/r" }, Targets = [tmp.Combine("t")] },
                new ConduitEntry { Name = "ok",    Source = new GitHubSkillSource { Repo = "o/r" },    Targets = [tmp.Combine("t")] },
            ],
        };

        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { StopOnFirstError = true, MaxParallelism = 1 });

        report.Entries.Should().HaveCount(2);
        report.Entries[0].Entry.Name.Should().Be("fails");
        report.Entries[0].Succeeded.Should().BeFalse();
        // The second entry should be present in the report (so the user knows
        // it wasn't processed) but marked as skipped, not run.
        report.Entries[1].Entry.Name.Should().Be("ok");
        report.Entries[1].Skipped.Should().BeTrue();
    }

    [Fact]
    public async Task Skips_commit_pinned_entry_when_state_matches_and_targets_exist()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var pinnedCommit = "abc1234567890abcdef1234567890abcdef12345";
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "pinned",
                    Source = new GitHubSkillSource { Repo = "o/r", Commit = pinnedCommit },
                    Targets = [tmp.Combine("target")],
                }
            ],
        };

        // First sync: should fetch + write state.
        (await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 })).Succeeded.Should().BeTrue();
        fetcher.FetchCount.Should().Be(1);

        // Second sync: state matches, target dir exists -> skip without fetching.
        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        report.Succeeded.Should().BeTrue();
        report.Entries[0].Skipped.Should().BeTrue();
        report.Entries[0].ResolvedRef.Should().Be(pinnedCommit);
        fetcher.FetchCount.Should().Be(1, because: "the state file should have proven the entry is up-to-date");
    }

    [Fact]
    public async Task Force_bypasses_the_state_based_skip()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "pinned",
                    Source = new GitHubSkillSource { Repo = "o/r", Commit = "abc1234567890abcdef1234567890abcdef12345" },
                    Targets = [tmp.Combine("target")],
                }
            ],
        };

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        fetcher.FetchCount.Should().Be(1);

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1, Force = true });

        fetcher.FetchCount.Should().Be(2, because: "--force must invalidate the cached state and re-fetch");
    }

    [Fact]
    public async Task Skip_invalidates_when_target_directory_has_been_deleted()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "pinned",
                    Source = new GitHubSkillSource { Repo = "o/r", Commit = "abc1234567890abcdef1234567890abcdef12345" },
                    Targets = [tmp.Combine("target")],
                }
            ],
        };

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        fetcher.FetchCount.Should().Be(1);

        Directory.Delete(tmp.Combine("target"), recursive: true);

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });

        fetcher.FetchCount.Should().Be(2, because: "missing targets should invalidate the cache and trigger a fresh fetch");
        Directory.Exists(tmp.Combine("target", "pinned")).Should().BeTrue();
    }

    [Fact]
    public async Task Skips_local_source_when_content_hash_matches_and_targets_exist()
    {
        using var tmp = new TempDir();

        var sourceDir = tmp.Combine("src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "v1");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "local",
                    Source = new LocalDirectorySkillSource { Path = sourceDir },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var sync = services.BuildServiceProvider().GetRequiredService<IConduitSynchronizer>();

        (await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 })).Succeeded.Should().BeTrue();
        File.ReadAllText(tmp.Combine("dest", "local", "SKILL.md")).Should().Be("v1");

        // Second run with no source changes should be a no-op (skipped).
        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        report.Entries[0].Skipped.Should().BeTrue(because: "the local source hash should match and the target should still exist");
    }

    [Fact]
    public async Task Re_syncs_local_source_when_a_file_inside_it_changes()
    {
        using var tmp = new TempDir();

        var sourceDir = tmp.Combine("src");
        Directory.CreateDirectory(sourceDir);
        var skillFile = Path.Combine(sourceDir, "SKILL.md");
        await File.WriteAllTextAsync(skillFile, "v1");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "local",
                    Source = new LocalDirectorySkillSource { Path = sourceDir },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var sync = services.BuildServiceProvider().GetRequiredService<IConduitSynchronizer>();

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        File.ReadAllText(tmp.Combine("dest", "local", "SKILL.md")).Should().Be("v1");

        // Edit the source. Ensure the LastWriteTimeUtc moves forward enough
        // for the hash to differ (Windows filesystem timestamp resolution).
        await Task.Delay(50);
        await File.WriteAllTextAsync(skillFile, "v2");

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        report.Succeeded.Should().BeTrue();
        report.Entries[0].Skipped.Should().BeFalse(because: "a content change must invalidate the local hash and trigger a re-mirror");

        File.ReadAllText(tmp.Combine("dest", "local", "SKILL.md")).Should().Be("v2");
    }

    [Fact]
    public async Task Re_syncs_local_source_when_a_target_was_deleted_even_if_source_unchanged()
    {
        using var tmp = new TempDir();

        var sourceDir = tmp.Combine("src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "x");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "local",
                    Source = new LocalDirectorySkillSource { Path = sourceDir },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var sync = services.BuildServiceProvider().GetRequiredService<IConduitSynchronizer>();

        await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });

        Directory.Delete(tmp.Combine("dest", "local"), recursive: true);

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 });
        report.Succeeded.Should().BeTrue();
        report.Entries[0].Skipped.Should().BeFalse(because: "even with an unchanged source, missing targets must invalidate the skip");
        File.Exists(tmp.Combine("dest", "local", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Parallel_sync_processes_many_entries_concurrently()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var entries = Enumerable.Range(0, 8).Select(i => new ConduitEntry
        {
            Name = $"e{i}",
            Source = new GitHubSkillSource { Repo = $"o/r{i}" },
            Targets = [tmp.Combine("t")],
        }).ToArray();

        var manifest = new ConduitManifest { Entries = entries };

        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 4 });

        report.Succeeded.Should().BeTrue();
        report.Entries.Should().HaveCount(8);
        report.Entries.Select(e => e.Entry.Name).Should().Equal(entries.Select(e => e.Name));
        fetcher.FetchCount.Should().Be(8);
    }

    [Fact]
    public async Task Per_target_alias_overrides_entry_name_in_destination()
    {
        using var tmp = new TempDir();
        var (sync, fetcher, _) = Build();

        fetcher.ContentProvider = _ => new Dictionary<string, string>
        {
            ["SKILL.md"] = "content",
        };

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "originalName",
                    Source = new GitHubSkillSource { Repo = "o/r" },
                    Targets =
                    [
                        new PathSpec(tmp.Combine("t1")),                                  // no alias -> originalName
                        new PathSpec(tmp.Combine("t2"), As: "renamed"),                   // alias  -> renamed
                    ],
                },
            ],
        };

        (await sync.SyncAsync(manifest, manifestPath, new SyncOptions { MaxParallelism = 1 })).Succeeded.Should().BeTrue();

        File.ReadAllText(tmp.Combine("t1", "originalName", "SKILL.md")).Should().Be("content");
        File.ReadAllText(tmp.Combine("t2", "renamed", "SKILL.md")).Should().Be("content");
        Directory.Exists(tmp.Combine("t2", "originalName")).Should().BeFalse();
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
            new JsonConduitStateStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConduitStateStore>.Instance),
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
                    Source = new GitHubSkillSource { Repo = "o/r" },
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
            Task.FromResult(FetchedSource.FromSingleDirectory(directory, source, resolvedRef: null, cleanup: null));
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
            return Task.FromResult(FetchedSource.FromSingleDirectory(dir, source, resolvedRef: null, cleanup: () =>
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
                return ValueTask.CompletedTask;
            }));
        }
    }
}
