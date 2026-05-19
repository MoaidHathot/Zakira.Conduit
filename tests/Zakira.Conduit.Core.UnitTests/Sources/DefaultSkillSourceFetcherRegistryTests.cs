using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class DefaultSkillSourceFetcherRegistryTests
{
    private sealed class StubFetcher(string kind) : ISkillSourceFetcher
    {
        public string SourceKind => kind;

        public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void Resolves_fetcher_by_kind()
    {
        var fetcher = new StubFetcher("github");
        var registry = new DefaultSkillSourceFetcherRegistry([fetcher]);

        registry.GetFetcher(new GitHubSkillSource { Owner = "o", Repo = "r" }).Should().BeSameAs(fetcher);
    }

    [Fact]
    public void Throws_when_two_fetchers_share_a_kind()
    {
        var act = () => new DefaultSkillSourceFetcherRegistry([new StubFetcher("github"), new StubFetcher("github")]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*github*");
    }

    [Fact]
    public void Throws_when_unknown_kind_is_requested()
    {
        var registry = new DefaultSkillSourceFetcherRegistry([new StubFetcher("github")]);

        var act = () => registry.GetFetcher(new UnknownSource());
        act.Should().Throw<NotSupportedException>().WithMessage("*unknown*");
    }

    private sealed record UnknownSource : ISkillSource
    {
        public string Kind => "unknown";
    }
}
