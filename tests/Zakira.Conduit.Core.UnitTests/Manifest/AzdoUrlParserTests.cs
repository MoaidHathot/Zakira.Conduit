using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class AzdoUrlParserTests
{
    [Theory]
    [InlineData("https://dev.azure.com/contoso/Conduit/_git/agent-skills", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/contoso/Conduit/_git/agent-skills.git", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/contoso/Conduit/_git/agent-skills/", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("https://contoso@dev.azure.com/contoso/Conduit/_git/agent-skills", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("https://contoso.visualstudio.com/Conduit/_git/agent-skills", "contoso", "Conduit", "agent-skills", "https://contoso.visualstudio.com/")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/Conduit/agent-skills", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/Conduit/agent-skills.git", "contoso", "Conduit", "agent-skills", "https://dev.azure.com/")]
    [InlineData("https://devops.contoso.internal/MyCollection/Conduit/_git/agent-skills", "MyCollection", "Conduit", "agent-skills", "https://devops.contoso.internal/")]
    public void Recognises_supported_forms(string input, string expectedOrg, string expectedProject, string expectedRepo, string expectedBase)
    {
        var c = AzdoUrlParser.Parse(input);
        c.Organization.Should().Be(expectedOrg);
        c.Project.Should().Be(expectedProject);
        c.Repo.Should().Be(expectedRepo);
        c.BaseUrl.AbsoluteUri.Should().Be(expectedBase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/org/project")]
    [InlineData("ftp://dev.azure.com/org/project/_git/repo")]
    public void Rejects_invalid_inputs(string input)
    {
        var act = () => AzdoUrlParser.Parse(input);
        act.Should().Throw<FormatException>();

        AzdoUrlParser.TryParse(input, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        AzdoUrlParser.TryParse(null, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }
}
