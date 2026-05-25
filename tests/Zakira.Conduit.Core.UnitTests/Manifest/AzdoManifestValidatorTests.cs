using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class AzdoManifestValidatorTests
{
    private static readonly string[] AuthEnvBogus = { "env", "bogus" };

    private static ConduitManifest Wrap(AzdoSkillSource source, string name = "ok") =>
        new()
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = name,
                    Source = source,
                    Targets = ["./out"],
                },
            ],
        };

    [Fact]
    public void Url_form_is_accepted()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
        });

        ManifestValidator.Validate(m).Should().BeEmpty();
    }

    [Fact]
    public void Triplet_form_is_accepted()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Organization = "contoso",
            Project = "Conduit",
            Repo = "agent-skills",
            Branch = "main",
        });

        ManifestValidator.Validate(m).Should().BeEmpty();
    }

    [Fact]
    public void Url_and_triplet_together_are_rejected()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Organization = "contoso",
            Project = "Conduit",
            Repo = "agent-skills",
            Branch = "main",
        });

        ManifestValidator.Validate(m).Should().ContainMatch("*mutually exclusive*");
    }

    [Fact]
    public void Missing_url_and_triplet_is_rejected()
    {
        var m = Wrap(new AzdoSkillSource { Branch = "main" });
        ManifestValidator.Validate(m).Should().ContainMatch("*provide either*");
    }

    [Fact]
    public void Branch_and_tag_together_are_rejected()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Tag = "v1",
        });

        ManifestValidator.Validate(m).Should().ContainMatch("*'branch' and 'tag' are mutually exclusive*");
    }

    [Fact]
    public void Branch_and_commit_together_are_allowed()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Commit = "abc123",
        });

        ManifestValidator.Validate(m).Should().BeEmpty();
    }

    [Fact]
    public void Unknown_auth_mode_is_rejected()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Auth = AuthEnvBogus,
        });

        ManifestValidator.Validate(m).Should().ContainMatch("*unknown mode 'bogus'*");
    }

    [Fact]
    public void Invalid_url_is_rejected()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "not-a-url",
            Branch = "main",
        });

        ManifestValidator.Validate(m).Should().ContainMatch("*url*");
    }

    [Fact]
    public void Sub_path_with_dotdot_is_rejected()
    {
        var m = Wrap(new AzdoSkillSource
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Path = "../escape",
        });

        ManifestValidator.Validate(m).Should().ContainMatch("*'..'*");
    }
}
