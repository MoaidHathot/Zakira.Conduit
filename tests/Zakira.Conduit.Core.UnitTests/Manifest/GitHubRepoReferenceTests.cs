using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class GitHubRepoReferenceTests
{
    [Theory]
    [InlineData("anthropics/skills", "anthropics", "skills")]
    [InlineData("ANTHROPICS/Skills", "ANTHROPICS", "Skills")]
    [InlineData("https://github.com/anthropics/skills", "anthropics", "skills")]
    [InlineData("https://github.com/anthropics/skills.git", "anthropics", "skills")]
    [InlineData("https://github.com/anthropics/skills/", "anthropics", "skills")]
    [InlineData("HTTPS://GitHub.com/anthropics/skills", "anthropics", "skills")]
    [InlineData("http://github.com/anthropics/skills", "anthropics", "skills")]
    [InlineData("github.com/anthropics/skills", "anthropics", "skills")]
    [InlineData("git@github.com:anthropics/skills.git", "anthropics", "skills")]
    [InlineData("git@github.com:anthropics/skills", "anthropics", "skills")]
    [InlineData("ssh://git@github.com/anthropics/skills.git", "anthropics", "skills")]
    [InlineData("  anthropics/skills  ", "anthropics", "skills")]
    [InlineData("owner.with.dots/repo-with-dashes_and_under.scores", "owner.with.dots", "repo-with-dashes_and_under.scores")]
    public void Recognises_supported_forms(string input, string expectedOwner, string expectedName)
    {
        var (owner, name) = GitHubRepoReference.Parse(input);
        owner.Should().Be(expectedOwner);
        name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-slug")]
    [InlineData("owner/")]
    [InlineData("/repo")]
    [InlineData("owner/repo/tree/main")]
    [InlineData("owner/repo with spaces")]
    [InlineData("owner!bad/repo")]
    [InlineData("owner/repo!bad")]
    public void Rejects_invalid_inputs(string input)
    {
        var act = () => GitHubRepoReference.Parse(input);
        act.Should().Throw<FormatException>();

        GitHubRepoReference.TryParse(input, out _, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Null_input_is_an_invalid_reference()
    {
        GitHubRepoReference.TryParse(null, out _, out _, out var error).Should().BeFalse();
        error.Should().Contain("empty", because: "null should be treated as 'no reference provided'");
    }
}
