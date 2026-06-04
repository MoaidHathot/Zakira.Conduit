using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.GitHub;
using Zakira.Conduit.Sources.GitHub.Credentials;
using GhExplicitPat = Zakira.Conduit.Sources.GitHub.Credentials.ExplicitPatCredentialProvider;
using GhAnonymous = Zakira.Conduit.Sources.GitHub.Credentials.AnonymousCredentialProvider;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class GitHubCredentialProviderTests
{
    private static readonly string[] OnlyEnv = { "env" };
    private static readonly string[] OnlyPat = { "pat" };
    private static readonly string[] OnlyAnonymous = { "anonymous" };
    private static readonly string[] OnlyGh = { "gh" };
    private static readonly string[] AnonymousThenEnv = { "anonymous", "env" };
    private static readonly string[] BogusChain = { "bogus" };
    private static readonly string[] DefaultChain = { "env", "gh", "anonymous" };

    private static GitHubSource Source(string[]? auth = null, string? patEnv = null) =>
        new()
        {
            Repo = "owner/repo",
            Branch = "main",
            Auth = auth,
            PatEnv = patEnv,
        };

    [Fact]
    public async Task EnvironmentToken_picks_CONDUIT_GITHUB_TOKEN_first()
    {
        var env = new FakeEnvironment()
            .Set("CONDUIT_GITHUB_TOKEN", "conduit-tok")
            .Set("GITHUB_TOKEN", "gh-tok")
            .Set("GH_TOKEN", "ghcli-tok");

        var provider = new EnvironmentTokenCredentialProvider(env);
        var header = await provider.TryGetAsync(Source());

        header.Should().NotBeNull();
        header!.Scheme.Should().Be("Bearer");
        header.Parameter.Should().Be("conduit-tok");
    }

    [Fact]
    public async Task EnvironmentToken_falls_through_to_GH_TOKEN()
    {
        var env = new FakeEnvironment().Set("GH_TOKEN", "ghcli-tok");
        var provider = new EnvironmentTokenCredentialProvider(env);

        var header = await provider.TryGetAsync(Source());
        header!.Parameter.Should().Be("ghcli-tok");
    }

    [Fact]
    public async Task EnvironmentToken_returns_null_when_unset()
    {
        var env = new FakeEnvironment();
        var provider = new EnvironmentTokenCredentialProvider(env);

        var header = await provider.TryGetAsync(Source());
        header.Should().BeNull();
    }

    [Fact]
    public async Task ExplicitPat_uses_custom_env_var_name()
    {
        var env = new FakeEnvironment().Set("MY_GH_TOKEN", "from-custom");
        var provider = new GhExplicitPat(env);

        var header = await provider.TryGetAsync(Source(patEnv: "MY_GH_TOKEN"));
        header!.Parameter.Should().Be("from-custom");
    }

    [Fact]
    public async Task ExplicitPat_defaults_to_CONDUIT_GITHUB_TOKEN_when_PatEnv_unset()
    {
        var env = new FakeEnvironment().Set("CONDUIT_GITHUB_TOKEN", "default-var");
        var provider = new GhExplicitPat(env);

        var header = await provider.TryGetAsync(Source());
        header!.Parameter.Should().Be("default-var");
    }

    [Fact]
    public async Task Chain_returns_first_non_null()
    {
        var env = new FakeEnvironment().Set("GITHUB_TOKEN", "envtok");

        var providers = new IGitHubCredentialProvider[]
        {
            new EnvironmentTokenCredentialProvider(env),
            new GhExplicitPat(env),
            new GhAnonymous(),
        };

        var chain = new ChainedGitHubCredentialProvider(providers, NullLogger<ChainedGitHubCredentialProvider>.Instance);
        var header = await chain.TryGetAsync(Source(auth: OnlyEnv));
        header!.Parameter.Should().Be("envtok");
    }

    [Fact]
    public async Task Chain_anonymous_short_circuits_with_null()
    {
        var env = new FakeEnvironment().Set("GITHUB_TOKEN", "envtok");

        var providers = new IGitHubCredentialProvider[]
        {
            new EnvironmentTokenCredentialProvider(env),
            new GhAnonymous(),
        };

        var chain = new ChainedGitHubCredentialProvider(providers, NullLogger<ChainedGitHubCredentialProvider>.Instance);
        // Anonymous-first explicitly opts out even though env would have answered.
        var header = await chain.TryGetAsync(Source(auth: AnonymousThenEnv));
        header.Should().BeNull();
    }

    [Fact]
    public async Task Chain_unknown_mode_throws()
    {
        var providers = new IGitHubCredentialProvider[]
        {
            new EnvironmentTokenCredentialProvider(new FakeEnvironment()),
        };

        var chain = new ChainedGitHubCredentialProvider(providers, NullLogger<ChainedGitHubCredentialProvider>.Instance);
        var act = async () => await chain.TryGetAsync(Source(auth: BogusChain));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OptionsToken_provider_returns_token_from_options()
    {
        var opts = Options.Create(new GitHubFetcherOptions { Token = "from-options" });
        var provider = new OptionsTokenCredentialProvider(opts);

        var header = await provider.TryGetAsync(Source());
        header!.Parameter.Should().Be("from-options");
    }

    [Fact]
    public void Default_chain_falls_back_when_Auth_unset()
    {
        var src = new GitHubSource { Repo = "o/r" };
        src.ResolvedAuthChain.Should().BeEquivalentTo(DefaultChain);
    }

    [Fact]
    public void Explicit_chain_wins_over_default()
    {
        var src = new GitHubSource { Repo = "o/r", Auth = OnlyPat };
        src.ResolvedAuthChain.Should().BeEquivalentTo(OnlyPat);
    }

    [Fact]
    public void Single_mode_chain_works()
    {
        var src = new GitHubSource { Repo = "o/r", Auth = OnlyGh };
        src.ResolvedAuthChain.Should().BeEquivalentTo(OnlyGh);
    }

    [Fact]
    public void Anonymous_only_chain_works()
    {
        var src = new GitHubSource { Repo = "o/r", Auth = OnlyAnonymous };
        src.ResolvedAuthChain.Should().BeEquivalentTo(OnlyAnonymous);
    }
}
