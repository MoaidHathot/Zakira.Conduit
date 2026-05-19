using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.IntegrationTests;

public sealed class LocalDirectorySourceSyncTests
{
    [Fact]
    public async Task Mirrors_a_local_directory_into_each_target_subdir()
    {
        using var tmp = new TempDir();

        var sourceDir = tmp.Combine("source-skills", "code-review");
        Directory.CreateDirectory(Path.Combine(sourceDir, "checks"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "# Code Review");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "checks", "a.json"), "{}");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "review",
                    Source = new LocalDirectorySkillSource { Path = "./source-skills/code-review" },
                    Targets = [tmp.Combine("agentA"), tmp.Combine("agentB")],
                },
            ],
        };

        var sync = BuildSynchronizer();
        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Succeeded.Should().BeTrue();
        File.ReadAllText(tmp.Combine("agentA", "review", "SKILL.md")).Should().Be("# Code Review");
        File.ReadAllText(tmp.Combine("agentA", "review", "checks", "a.json")).Should().Be("{}");
        File.ReadAllText(tmp.Combine("agentB", "review", "SKILL.md")).Should().Be("# Code Review");
    }

    [Fact]
    public async Task Re_syncing_after_source_changes_removes_stale_files_and_keeps_target_in_sync()
    {
        using var tmp = new TempDir();

        var sourceDir = tmp.Combine("source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "v1");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "legacy.md"), "remove me");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "skill",
                    Source = new LocalDirectorySkillSource { Path = sourceDir },
                    Targets = [tmp.Combine("dest")],
                },
            ],
        };

        var sync = BuildSynchronizer();

        (await sync.SyncAsync(manifest, manifestPath, new SyncOptions())).Succeeded.Should().BeTrue();
        File.Exists(tmp.Combine("dest", "skill", "legacy.md")).Should().BeTrue();

        // Mutate the source: edit one file, remove another, add a third.
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "SKILL.md"), "v2");
        File.Delete(Path.Combine(sourceDir, "legacy.md"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "NEW.md"), "added");

        (await sync.SyncAsync(manifest, manifestPath, new SyncOptions())).Succeeded.Should().BeTrue();

        File.ReadAllText(tmp.Combine("dest", "skill", "SKILL.md")).Should().Be("v2");
        File.Exists(tmp.Combine("dest", "skill", "legacy.md")).Should().BeFalse();
        File.Exists(tmp.Combine("dest", "skill", "NEW.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Missing_source_directory_is_reported_per_entry_without_aborting_others()
    {
        using var tmp = new TempDir();

        // entry 'good' has a real source; entry 'bad' does not.
        var goodSource = tmp.Combine("good");
        Directory.CreateDirectory(goodSource);
        await File.WriteAllTextAsync(Path.Combine(goodSource, "SKILL.md"), "ok");

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "bad",
                    Source = new LocalDirectorySkillSource { Path = tmp.Combine("does-not-exist") },
                    Targets = [tmp.Combine("dest")],
                },
                new ConduitEntry
                {
                    Name = "good",
                    Source = new LocalDirectorySkillSource { Path = goodSource },
                    Targets = [tmp.Combine("dest")],
                },
            ],
        };

        var sync = BuildSynchronizer();
        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Succeeded.Should().BeFalse();
        report.Entries.Should().HaveCount(2);
        report.Entries.Single(e => e.Entry.Name == "bad").Succeeded.Should().BeFalse();
        report.Entries.Single(e => e.Entry.Name == "good").Succeeded.Should().BeTrue();
        File.ReadAllText(tmp.Combine("dest", "good", "SKILL.md")).Should().Be("ok");
    }

    private static IConduitSynchronizer BuildSynchronizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IConduitSynchronizer>();
    }
}
