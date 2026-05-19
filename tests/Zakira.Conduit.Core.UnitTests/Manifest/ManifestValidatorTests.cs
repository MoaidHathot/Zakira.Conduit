using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class ManifestValidatorTests
{
    private static ConduitEntry ValidEntry(string name = "ok") => new()
    {
        Name = name,
        Source = new GitHubSkillSource { Owner = "o", Repo = "r" },
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

    [Fact]
    public void GitHub_source_requires_owner_and_repo()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Owner = "", Repo = "" },
                    Targets = ["./o"],
                }
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().ContainMatch("*owner*");
        errors.Should().ContainMatch("*repo*");
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
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r", Commit = "abc", Branch = "main" },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*mutually exclusive*");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("foo/../bar")]
    public void Source_path_must_be_relative_and_safe(string path)
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new GitHubSkillSource { Owner = "o", Repo = "r", Path = path },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*repository-relative*");
    }

    [Fact]
    public void Valid_manifest_returns_no_errors()
    {
        var manifest = new ConduitManifest { Entries = [ValidEntry()] };
        ManifestValidator.Validate(manifest).Should().BeEmpty();
    }

    [Fact]
    public void Local_source_requires_a_path()
    {
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "x",
                    Source = new LocalDirectorySkillSource { Path = "" },
                    Targets = ["./o"],
                }
            ],
        };

        ManifestValidator.Validate(manifest).Should().ContainMatch("*source.path must be a non-empty string*");
    }

    [Fact]
    public void Local_source_with_a_path_is_accepted()
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
}
