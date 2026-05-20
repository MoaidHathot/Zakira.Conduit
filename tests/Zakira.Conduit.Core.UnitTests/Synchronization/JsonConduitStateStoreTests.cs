using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.Core.UnitTests.Synchronization;

public sealed class JsonConduitStateStoreTests
{
    [Fact]
    public async Task Returns_empty_state_when_no_file_exists()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = await store.LoadAsync(manifestPath);

        state.Entries.Should().BeEmpty();
        state.Version.Should().Be(1);
    }

    [Fact]
    public async Task Returns_empty_state_when_file_is_corrupt()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(Path.Combine(tmp.Path, JsonConduitStateStore.StateFileName), "this { is not } valid json");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = await store.LoadAsync(manifestPath);

        state.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Roundtrips_an_entry_state_to_disk_atomically()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = await store.LoadAsync(manifestPath);

        store.UpdateEntry(state, "alpha", new EntryState
        {
            ResolvedRef = "abc123",
            Etag = "W/\"etag-1\"",
            LastSyncUtc = new DateTimeOffset(2025, 5, 20, 12, 0, 0, TimeSpan.Zero),
            Targets = [tmp.Combine("out", "alpha")],
        });

        await store.SaveAsync(manifestPath, state);

        var path = store.GetStateFilePath(manifestPath);
        File.Exists(path).Should().BeTrue();

        var reloaded = await store.LoadAsync(manifestPath);
        var entry = reloaded.Entries.Should().ContainKey("alpha").WhoseValue;
        entry.ResolvedRef.Should().Be("abc123");
        entry.Etag.Should().Be("W/\"etag-1\"");
        entry.LastSyncUtc.Should().Be(new DateTimeOffset(2025, 5, 20, 12, 0, 0, TimeSpan.Zero));
        entry.Targets.Should().ContainSingle().Which.Should().Be(tmp.Combine("out", "alpha"));
    }

    [Fact]
    public async Task Save_leaves_no_tmp_files_behind()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");

        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = await store.LoadAsync(manifestPath);
        store.UpdateEntry(state, "x", new EntryState { ResolvedRef = "1" });

        await store.SaveAsync(manifestPath, state);

        Directory.EnumerateFiles(tmp.Path, "*.tmp-*").Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateEntry_and_GetEntry_are_consistent_under_concurrent_writers()
    {
        using var tmp = new TempDir();
        var manifestPath = tmp.Combine("conduit.json");
        var store = new JsonConduitStateStore(NullLogger<JsonConduitStateStore>.Instance);
        var state = await store.LoadAsync(manifestPath);

        // 8 parallel writers each updating their own key 100 times.
        await Parallel.ForEachAsync(Enumerable.Range(0, 8), new ParallelOptions { MaxDegreeOfParallelism = 8 }, (i, ct) =>
        {
            for (var j = 0; j < 100; j++)
            {
                store.UpdateEntry(state, $"entry-{i}", new EntryState { ResolvedRef = j.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < 8; i++)
        {
            store.GetEntry(state, $"entry-{i}")!.ResolvedRef.Should().Be("99");
        }
    }
}
