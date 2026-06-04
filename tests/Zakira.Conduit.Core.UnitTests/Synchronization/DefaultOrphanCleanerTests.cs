using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Core.UnitTests.Synchronization;

public sealed class DefaultOrphanCleanerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DefaultOrphanCleaner BuildCleaner(IConduitStateStore store) =>
        new(store, new DefaultPathResolver(new FakeEnvironment()), NullLogger<DefaultOrphanCleaner>.Instance);

    [Fact]
    public async Task Removes_orphan_target_directories_and_prunes_state()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}"); // contents irrelevant; path is what matters

        var orphanTarget = Path.Combine(tmp.Path, "orphan-dir");
        Directory.CreateDirectory(orphanTarget);
        await File.WriteAllTextAsync(Path.Combine(orphanTarget, "stale.txt"), "x");

        var liveTarget = Path.Combine(tmp.Path, "live-dir");
        Directory.CreateDirectory(liveTarget);

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = new ConduitState
        {
            Entries =
            {
                ["dead"] = new EntryState { Targets = new[] { orphanTarget }, LastSyncUtc = Now },
                ["alive"] = new EntryState { Targets = new[] { liveTarget }, LastSyncUtc = Now },
            },
        };

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "alive",
                    Source = new LocalDirectorySource { Path = tmp.Path },
                    Targets = [new PathSpec(tmp.Path)],
                },
            ],
        };

        var cleaner = BuildCleaner(store);
        var report = await cleaner.CleanAsync(manifest, manifestPath, state, dryRun: false);

        Directory.Exists(orphanTarget).Should().BeFalse("orphan was deleted");
        Directory.Exists(liveTarget).Should().BeTrue("live entry's target is preserved");
        state.Entries.ContainsKey("dead").Should().BeFalse("orphan state entry pruned");
        state.Entries.ContainsKey("alive").Should().BeTrue("live state entry preserved");
        report.EntriesPruned.Should().Be(1);
        report.Results.Should().ContainSingle(r => r.EntryName == "dead" && r.Action == OrphanCleanupAction.Removed);
    }

    [Fact]
    public async Task DryRun_does_not_delete_anything_or_mutate_state()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var orphanTarget = Path.Combine(tmp.Path, "orphan-dir");
        Directory.CreateDirectory(orphanTarget);
        await File.WriteAllTextAsync(Path.Combine(orphanTarget, "stale.txt"), "x");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = new ConduitState
        {
            Entries = { ["dead"] = new EntryState { Targets = new[] { orphanTarget }, LastSyncUtc = Now } },
        };
        var manifest = new ConduitManifest { Entries = [] };

        var cleaner = BuildCleaner(store);
        var report = await cleaner.CleanAsync(manifest, manifestPath, state, dryRun: true);

        Directory.Exists(orphanTarget).Should().BeTrue("dry-run preserves the directory");
        state.Entries.ContainsKey("dead").Should().BeTrue("dry-run preserves state");
        report.EntriesPruned.Should().Be(0);
        report.Results.Should().ContainSingle(r => r.Action == OrphanCleanupAction.Removed && r.Message!.Contains("dry-run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_to_delete_when_dir_is_owned_by_a_live_entry()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var sharedDir = Path.Combine(tmp.Path, "shared");
        Directory.CreateDirectory(sharedDir);

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = new ConduitState
        {
            Entries =
            {
                ["renamed-from"] = new EntryState { Targets = new[] { sharedDir }, LastSyncUtc = Now },
            },
        };

        // The "renamed-to" entry now writes to that same dir via its name.
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "shared",
                    Source = new LocalDirectorySource { Path = tmp.Path },
                    Targets = [new PathSpec(tmp.Path)],
                },
            ],
        };

        var cleaner = BuildCleaner(store);
        var report = await cleaner.CleanAsync(manifest, manifestPath, state, dryRun: false);

        Directory.Exists(sharedDir).Should().BeTrue("dir still claimed by a live entry");
        report.Results.Should().ContainSingle(r => r.Action == OrphanCleanupAction.SkippedOwnedByLiveEntry);
        state.Entries.ContainsKey("renamed-from").Should().BeFalse("orphan state row pruned because the dir is now owned by a live entry");
    }

    [Fact]
    public async Task Already_gone_target_just_prunes_state()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");
        var ghostDir = Path.Combine(tmp.Path, "never-existed");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = new ConduitState
        {
            Entries = { ["dead"] = new EntryState { Targets = new[] { ghostDir }, LastSyncUtc = Now } },
        };

        var report = await BuildCleaner(store).CleanAsync(new ConduitManifest { Entries = [] }, manifestPath, state, dryRun: false);

        report.Results.Should().ContainSingle(r => r.Action == OrphanCleanupAction.AlreadyGone);
        state.Entries.ContainsKey("dead").Should().BeFalse();
        report.EntriesPruned.Should().Be(1);
    }
}
