using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class ResolvedDestinationValidatorTests
{
    [Fact]
    public void Catches_collision_when_two_targets_resolve_to_the_same_path_via_env_var()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        File.WriteAllText(manifestPath, "{}");

        var env = new FakeEnvironment();
        var home = OperatingSystem.IsWindows() ? @"C:\Users\fake" : "/home/fake";
        env.HomeDirectory = home;
        env.Set("HOME", home);

        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "first",
                    Source = new GitHubSource { Repo = "owner/repo1" },
                    Targets = [new PathSpec("~/myskills")],
                },
                new ConduitEntry
                {
                    Name = "second",
                    Source = new GitHubSource { Repo = "owner/repo2" },
                    Targets = [new PathSpec("$HOME/myskills", "first")],
                },
            ],
        };

        var validator = new ResolvedDestinationValidator(new DefaultPathResolver(env));
        var errors = validator.Validate(manifest, manifestPath);

        errors.Should().ContainSingle();
        errors[0].Should().Contain("first");
        errors[0].Should().Contain("myskills");
    }

    [Fact]
    public void No_collision_for_distinct_resolved_paths()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        File.WriteAllText(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "alpha",
                    Source = new GitHubSource { Repo = "owner/r1" },
                    Targets = [new PathSpec(tmp.Path)],
                },
                new ConduitEntry
                {
                    Name = "beta",
                    Source = new GitHubSource { Repo = "owner/r2" },
                    Targets = [new PathSpec(tmp.Path)],
                },
            ],
        };

        var validator = new ResolvedDestinationValidator(new DefaultPathResolver(new FakeEnvironment()));
        var errors = validator.Validate(manifest, manifestPath);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Self_collision_within_an_entrys_own_targets_is_not_double_reported()
    {
        using var tmp = new TempDir();
        var manifestPath = Path.Combine(tmp.Path, "conduit.json");
        File.WriteAllText(manifestPath, "{}");

        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSource { Repo = "owner/r" },
                    // Two targets, both with the same alias under the same parent dir.
                    // This is a single-entry self-overlap; the resolved validator
                    // is about CROSS-entry collisions, so it should stay silent.
                    Targets =
                    [
                        new PathSpec(tmp.Path, "shared"),
                        new PathSpec(tmp.Path, "shared"),
                    ],
                },
            ],
        };

        var validator = new ResolvedDestinationValidator(new DefaultPathResolver(new FakeEnvironment()));
        var errors = validator.Validate(manifest, manifestPath);

        errors.Should().BeEmpty();
    }
}
