using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Azdo;
using Zakira.Conduit.Sources.Azdo.Credentials;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class AzdoCredentialProviderTests
{
    private static readonly string[] AuthEnvAz = { "env", "az" };
    private static readonly string[] AuthEnv = { "env" };
    private static readonly string[] AuthBogus = { "bogus" };
    private static readonly string[] AuthAnonymousEnv = { "anonymous", "env" };

    private static AzdoSkillSource Source(string[]? auth = null, string? patEnv = null) =>
        new()
        {
            Url = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Auth = auth,
            PatEnv = patEnv,
        };

    [Fact]
    public async Task EnvironmentPat_picks_first_available_var()
    {
        var env = new FakeEnvironment()
            .Set("CONDUIT_AZDO_TOKEN", "tok1")
            .Set("AZURE_DEVOPS_EXT_PAT", "tok2");

        var provider = new EnvironmentPatCredentialProvider(env);
        var header = await provider.TryGetAsync(Source());

        header.Should().NotBeNull();
        header!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task EnvironmentPat_returns_null_when_no_vars_set()
    {
        var env = new FakeEnvironment();
        var provider = new EnvironmentPatCredentialProvider(env);

        var header = await provider.TryGetAsync(Source());
        header.Should().BeNull();
    }

    [Fact]
    public async Task EnvironmentPat_falls_back_through_candidates()
    {
        var env = new FakeEnvironment().Set("SYSTEM_ACCESSTOKEN", "pipeline-token");
        var provider = new EnvironmentPatCredentialProvider(env);

        var header = await provider.TryGetAsync(Source());
        header.Should().NotBeNull();
    }

    [Fact]
    public async Task ExplicitPat_reads_named_env_var()
    {
        var env = new FakeEnvironment().Set("MY_AZDO_TOKEN", "tok");
        var provider = new ExplicitPatCredentialProvider(env);

        var header = await provider.TryGetAsync(Source(patEnv: "MY_AZDO_TOKEN"));
        header.Should().NotBeNull();
        header!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task ExplicitPat_defaults_to_CONDUIT_AZDO_TOKEN()
    {
        var env = new FakeEnvironment().Set("CONDUIT_AZDO_TOKEN", "tok");
        var provider = new ExplicitPatCredentialProvider(env);

        var header = await provider.TryGetAsync(Source());
        header.Should().NotBeNull();
    }

    [Fact]
    public async Task Anonymous_always_returns_null()
    {
        var provider = new AnonymousCredentialProvider();
        (await provider.TryGetAsync(Source())).Should().BeNull();
    }

    [Fact]
    public async Task AzCli_returns_bearer_when_process_succeeds()
    {
        var runner = new FakeProcessRunner((file, args) => new ProcessResult(0, "the-token\n", string.Empty));
        var provider = new AzCliCredentialProvider(runner, Options.Create(new AzdoFetcherOptions()), NullLogger<AzCliCredentialProvider>.Instance);

        var header = await provider.TryGetAsync(Source());
        header.Should().NotBeNull();
        header!.Scheme.Should().Be("Bearer");
        header.Parameter.Should().Be("the-token");
    }

    [Fact]
    public async Task AzCli_returns_null_when_az_missing()
    {
        var runner = new FakeProcessRunner((file, args) => throw new FileNotFoundException("not found", "az"));
        var provider = new AzCliCredentialProvider(runner, Options.Create(new AzdoFetcherOptions()), NullLogger<AzCliCredentialProvider>.Instance);

        (await provider.TryGetAsync(Source())).Should().BeNull();
    }

    [Fact]
    public async Task AzCli_returns_null_when_az_fails()
    {
        var runner = new FakeProcessRunner((file, args) => new ProcessResult(1, string.Empty, "Please run 'az login'"));
        var provider = new AzCliCredentialProvider(runner, Options.Create(new AzdoFetcherOptions()), NullLogger<AzCliCredentialProvider>.Instance);

        (await provider.TryGetAsync(Source())).Should().BeNull();
    }

    [Fact]
    public async Task AzCli_caches_tokens_within_a_process()
    {
        var callCount = 0;
        var runner = new FakeProcessRunner((file, args) =>
        {
            callCount++;
            return new ProcessResult(0, "tok\n", string.Empty);
        });

        var provider = new AzCliCredentialProvider(runner, Options.Create(new AzdoFetcherOptions()), NullLogger<AzCliCredentialProvider>.Instance);
        await provider.TryGetAsync(Source());
        await provider.TryGetAsync(Source());
        await provider.TryGetAsync(Source());

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Chain_uses_first_yielding_provider()
    {
        var env = new FakeEnvironment().Set("CONDUIT_AZDO_TOKEN", "from-env");
        var envProv = new EnvironmentPatCredentialProvider(env);
        var azProv = new AzCliCredentialProvider(new FakeProcessRunner((_, _) => new ProcessResult(0, "from-az\n", string.Empty)),
            Options.Create(new AzdoFetcherOptions()),
            NullLogger<AzCliCredentialProvider>.Instance);

        var chain = new ChainedAzdoCredentialProvider(new IAzdoCredentialProvider[] { envProv, azProv }, NullLogger<ChainedAzdoCredentialProvider>.Instance);

        var header = await chain.TryGetAsync(Source(auth: AuthEnvAz));
        header!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task Chain_falls_through_when_provider_yields_null()
    {
        var env = new FakeEnvironment(); // no token
        var envProv = new EnvironmentPatCredentialProvider(env);
        var azProv = new AzCliCredentialProvider(new FakeProcessRunner((_, _) => new ProcessResult(0, "from-az\n", string.Empty)),
            Options.Create(new AzdoFetcherOptions()),
            NullLogger<AzCliCredentialProvider>.Instance);

        var chain = new ChainedAzdoCredentialProvider(new IAzdoCredentialProvider[] { envProv, azProv }, NullLogger<ChainedAzdoCredentialProvider>.Instance);

        var header = await chain.TryGetAsync(Source(auth: AuthEnvAz));
        header!.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task Chain_returns_null_when_everything_declines()
    {
        var env = new FakeEnvironment();
        var envProv = new EnvironmentPatCredentialProvider(env);
        var chain = new ChainedAzdoCredentialProvider(new IAzdoCredentialProvider[] { envProv }, NullLogger<ChainedAzdoCredentialProvider>.Instance);

        (await chain.TryGetAsync(Source(auth: AuthEnv))).Should().BeNull();
    }

    [Fact]
    public async Task Chain_throws_on_unknown_mode()
    {
        var chain = new ChainedAzdoCredentialProvider(new IAzdoCredentialProvider[] { new AnonymousCredentialProvider() }, NullLogger<ChainedAzdoCredentialProvider>.Instance);

        var act = async () => await chain.TryGetAsync(Source(auth: AuthBogus));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Chain_anonymous_short_circuits_to_no_auth()
    {
        var env = new FakeEnvironment().Set("CONDUIT_AZDO_TOKEN", "should-not-be-used");
        var envProv = new EnvironmentPatCredentialProvider(env);
        var chain = new ChainedAzdoCredentialProvider(
            new IAzdoCredentialProvider[] { new AnonymousCredentialProvider(), envProv },
            NullLogger<ChainedAzdoCredentialProvider>.Instance);

        (await chain.TryGetAsync(Source(auth: AuthAnonymousEnv))).Should().BeNull();
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<string, IReadOnlyList<string>, ProcessResult> _impl;

        public FakeProcessRunner(Func<string, IReadOnlyList<string>, ProcessResult> impl)
        {
            _impl = impl;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            try
            {
                return Task.FromResult(_impl(fileName, arguments));
            }
            catch (Exception ex)
            {
                return Task.FromException<ProcessResult>(ex);
            }
        }
    }
}
