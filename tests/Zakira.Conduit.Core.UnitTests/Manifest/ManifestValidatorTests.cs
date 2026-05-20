using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class ManifestValidatorTests
{
    private static ConduitEntry ValidEntry(string name = "ok") => new()
    {
        Name = name,
        Source = new GitHubSkillSource { Repo = "o/r" },
        Targets = ["./out"],
    };

    [Fact]
    public void Empty_entries_returns_error()
    {
        var manifest = new ConduitManifest { Entries = [] };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*no entries*");
    }

    [Fact]
    public void Unsupported_version_is_rejected()
    {
        var manifest = new ConduitManifest { Version = 9999, Entries = [ValidEntry()] };
        ManifestValidator.Validate(manifest).Should().ContainMatch("*Unsupported manifest version*");
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        var manifest = new ConduitManifest { Entries = [ValidEntry("dup"), ValidEntry("dup")] };
        ManifestValidator.Validate(manifest).Should().ContainMatch("*duplicated*");
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("path/separator")]
    [InlineData("colon:name")]
    public void Invalid_name_characters_are_rejected(string name)
    {
        var manifest = new ConduitManifest { Entries = [ValidEntry(name)] };
        ManifestValidator.Validate(manifest).Should().ContainMatch("*invalid characters*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("noslash")]
    [InlineData("/leading-slash")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner//double")]
    [InlineData("owner/repo with spaces")]
    public void GitHub_repo_field_must_be_a_recognized_reference(string repo)
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = repo },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*repo*");
    }

    [Theory]
    [InlineData("owner/repo")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo/")]
    [InlineData("github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git")]
    public void GitHub_repo_field_accepts_slug_and_url_forms(string repo)
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = repo },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().BeEmpty();
    }

    [Fact]
    public void Commit_and_branch_are_mutually_exclusive()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = "o/r", Commit = "abc", Branch = "main" },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*mutually exclusive*");
    }

    [Fact]
    public void Path_and_paths_are_mutually_exclusive_on_github()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = "o/r", Path = "a", Paths = ["b"] },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*'path' and 'paths' are mutually exclusive*");
    }

    [Fact]
    public void Path_and_paths_are_mutually_exclusive_on_local()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource { Path = "a", Paths = ["b"] },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*'path' and 'paths' are mutually exclusive*");
    }

    [Fact]
    public void Github_paths_with_duplicate_basenames_are_rejected()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = "o/r", Paths = ["dir1/foo", "dir2/foo"] },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*basename 'foo'*");
    }

    [Fact]
    public void Local_paths_with_duplicate_basenames_are_rejected()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource { Paths = ["./a/skill", "./b/skill"] },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*basename 'skill'*");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("foo/../bar")]
    public void Source_path_must_be_relative_and_safe_for_github(string path)
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Repo = "o/r", Path = path },
                    Targets = ["./o"],
                }
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Local_source_requires_at_least_one_path()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource(),
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*at least one*");
    }

    [Fact]
    public void Local_source_with_path_is_accepted()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource { Path = "./skills/foo" },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().BeEmpty();
    }

    [Fact]
    public void Local_source_with_paths_array_is_accepted()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource { Paths = ["./a", "./b"] },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().BeEmpty();
    }

    [Fact]
    public void Valid_manifest_returns_no_errors()
    {
        var manifest = new ConduitManifest { Entries = [ValidEntry()] };
        ManifestValidator.Validate(manifest).Should().BeEmpty();
    }
}
