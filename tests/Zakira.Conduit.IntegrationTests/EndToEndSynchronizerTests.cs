using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.IntegrationTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.IntegrationTests;

/// <summary>
///     Synchronizer + real <see cref="Sources.GitHub.GitHubSkillSourceFetcher"/>
///     against a local HTTP mock, covering the full download \u2192 extract \u2192
///     mirror pipeline.
/// </summary>
public sealed class EndToEndSynchronizerTests
{
    [Fact]
    public async Task Syncs_a_github_entry_into_target_subdirs()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var payload = ZipballPayload.Build("acme-skills-deadbeef", new Dictionary<string, string>
        {
            ["skills/review/SKILL.md"] = "# review",
            ["skills/review/data.json"] = "{}",
        });
        server.MapZipball("acme", "skills", gitRef: "main", payload);

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "review",
                    Source = new GitHubSkillSource
                    {
                        Owner = "acme",
                        Repo = "skills",
                        Path = "skills/review",
                        Branch = "main",
                    },
                    Targets = [tmp.Combine("targetA"), tmp.Combine("nested", "targetB")],
                },
            ],
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore(opts => opts.BaseAddress = server.BaseAddress);
        var provider = services.BuildServiceProvider();

        var sync = provider.GetRequiredService<IConduitSynchronizer>();
        var report = await sync.SyncAsync(manifest, manifestPath, new SyncOptions());

        report.Succeeded.Should().BeTrue();

        File.Exists(tmp.Combine("targetA", "review", "SKILL.md")).Should().BeTrue();
        File.Exists(tmp.Combine("targetA", "review", "data.json")).Should().BeTrue();
        File.Exists(tmp.Combine("nested", "targetB", "review", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Subsequent_sync_replaces_stale_files_in_targets()
    {
        using var tmp = new TempDir();
        await using var server = new MockGitHubServer();

        var v1 = ZipballPayload.Build("acme-skills-v1", new Dictionary<string, string>
        {
            ["SKILL.md"] = "v1",
            ["legacy.md"] = "to be removed",
        });
        var v2 = ZipballPayload.Build("acme-skills-v2", new Dictionary<string, string>
        {
            ["SKILL.md"] = "v2",
            ["NEW.md"] = "added",
        });

        server.MapZipball("acme", "skills", gitRef: "v1", v1);
        server.MapZipball("acme", "skills", gitRef: "v2", v2);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConduitCore(opts => opts.BaseAddress = server.BaseAddress);
        var provider = services.BuildServiceProvider();
        var sync = provider.GetRequiredService<IConduitSynchronizer>();

        var manifestPath = tmp.Combine("conduit.json");
        await File.WriteAllTextAsync(manifestPath, "{}");

        var entry1 = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "skills",
                    Source = new GitHubSkillSource { Owner = "acme", Repo = "skills", Branch = "v1" },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };
        var entry2 = entry1 with
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "skills",
                    Source = new GitHubSkillSource { Owner = "acme", Repo = "skills", Branch = "v2" },
                    Targets = [tmp.Combine("dest")],
                }
            ],
        };

        (await sync.SyncAsync(entry1, manifestPath, new SyncOptions())).Succeeded.Should().BeTrue();
        File.ReadAllText(tmp.Combine("dest", "skills", "SKILL.md")).Should().Be("v1");
        File.Exists(tmp.Combine("dest", "skills", "legacy.md")).Should().BeTrue();

        (await sync.SyncAsync(entry2, manifestPath, new SyncOptions())).Succeeded.Should().BeTrue();

        File.ReadAllText(tmp.Combine("dest", "skills", "SKILL.md")).Should().Be("v2");
        File.Exists(tmp.Combine("dest", "skills", "legacy.md")).Should().BeFalse("the staging-swap mirror must remove stale files");
        File.Exists(tmp.Combine("dest", "skills", "NEW.md")).Should().BeTrue();
    }
}
