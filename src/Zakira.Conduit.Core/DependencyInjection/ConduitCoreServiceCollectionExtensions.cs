using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.Azdo;
using Zakira.Conduit.Sources.Azdo.Credentials;
using Zakira.Conduit.Sources.GitHub;
using Zakira.Conduit.Sources.Inference;
using Zakira.Conduit.Sources.Local;
using Zakira.Conduit.Synchronization;

namespace Zakira.Conduit.DependencyInjection;

/// <summary>
///     Composition root for <c>Zakira.Conduit.Core</c>.
/// </summary>
public static class ConduitCoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the default services for loading, fetching and mirroring
    ///     conduit entries. The GitHub and local-directory fetchers are
    ///     registered by default; pass a callback to <paramref name="configureGitHub"/>
    ///     to customize the GitHub fetcher's options (e.g. set a token).
    /// </summary>
    public static IServiceCollection AddConduitCore(this IServiceCollection services, Action<GitHubFetcherOptions>? configureGitHub = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEnvironment, SystemEnvironment>();

        services.TryAddSingleton<IManifestLocator, DefaultManifestLocator>();
        services.TryAddSingleton<IManifestLoader>(sp =>
            new JsonManifestLoader(sp.GetService<SkillSourceInferenceCoordinator>()));
        services.TryAddSingleton<IManifestWriter, JsonNodeManifestWriter>();
        services.TryAddSingleton<IPathResolver, DefaultPathResolver>();
        services.TryAddSingleton<IDirectoryMirror, AtomicDirectoryMirror>();
        services.TryAddSingleton<ISkillSourceFetcherRegistry, DefaultSkillSourceFetcherRegistry>();
        services.TryAddSingleton<IConduitStateStore, JsonConduitStateStore>();
        services.TryAddSingleton<IConduitSynchronizer, DefaultConduitSynchronizer>();

        services.AddGitHubSkillSource(configureGitHub);
        services.AddLocalDirectorySkillSource();
        services.AddAzdoSkillSource();
        services.AddSkillSourceInference();

        return services;
    }

    /// <summary>
    ///     Registers the local-directory <see cref="ISkillSourceFetcher"/>.
    ///     Safe to call standalone if you only want the local source.
    /// </summary>
    public static IServiceCollection AddLocalDirectorySkillSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPathResolver, DefaultPathResolver>();
        services.AddSingleton<ISkillSourceFetcher, LocalDirectorySkillSourceFetcher>();

        return services;
    }

    /// <summary>
    ///     Registers the GitHub <see cref="ISkillSourceFetcher"/> and its
    ///     <see cref="HttpClient"/> with sensible defaults. Safe to call
    ///     standalone if you only want the GitHub source.
    /// </summary>
    public static IServiceCollection AddGitHubSkillSource(this IServiceCollection services, Action<GitHubFetcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<GitHubFetcherOptions>()
            .Configure(static opts =>
            {
                var envToken =
                    Environment.GetEnvironmentVariable("CONDUIT_GITHUB_TOKEN") ??
                    Environment.GetEnvironmentVariable("GITHUB_TOKEN");

                if (!string.IsNullOrWhiteSpace(envToken))
                {
                    opts.Token = envToken;
                }

                var envBase = Environment.GetEnvironmentVariable("CONDUIT_GITHUB_API_BASE");
                if (!string.IsNullOrWhiteSpace(envBase) && Uri.TryCreate(envBase, UriKind.Absolute, out var parsed))
                {
                    opts.BaseAddress = parsed;
                }
            });

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHttpClient<IGitHubArchiveDownloader, GitHubArchiveDownloader>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<GitHubFetcherOptions>>().Value;
                client.BaseAddress = opts.BaseAddress;
                client.Timeout = TimeSpan.FromSeconds(60);
            });

        services.AddHttpClient<IGitHubRefResolver, GitHubRefResolver>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<GitHubFetcherOptions>>().Value;
                client.BaseAddress = opts.BaseAddress;
                client.Timeout = TimeSpan.FromSeconds(60);
            });

        services.AddSingleton<ISkillSourceFetcher, GitHubSkillSourceFetcher>();

        return services;
    }

    /// <summary>
    ///     Registers the Azure DevOps <see cref="ISkillSourceFetcher"/> and its
    ///     <see cref="HttpClient"/>s with sensible defaults. Safe to call
    ///     standalone if you only want the AzDO source.
    /// </summary>
    public static IServiceCollection AddAzdoSkillSource(this IServiceCollection services, Action<AzdoFetcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEnvironment, SystemEnvironment>();

        services.AddOptions<AzdoFetcherOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Credential providers registered in canonical order; the chain itself
        // is driven by AzdoSkillSource.ResolvedAuthChain at call time.
        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IAzdoCredentialProvider, EnvironmentPatCredentialProvider>();
        services.AddSingleton<IAzdoCredentialProvider, AzCliCredentialProvider>();
        services.AddSingleton<IAzdoCredentialProvider, ExplicitPatCredentialProvider>();
        services.AddSingleton<IAzdoCredentialProvider, AnonymousCredentialProvider>();
        services.AddSingleton<ChainedAzdoCredentialProvider>();

        services.AddHttpClient<IAzdoRefResolver, AzdoRefResolver>()
            .ConfigureHttpClient(static (sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
            });

        services.AddHttpClient<IAzdoItemsArchiveDownloader, AzdoItemsArchiveDownloader>()
            .ConfigureHttpClient(static (sp, client) =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });

        services.AddSingleton<ISkillSourceFetcher, AzdoSkillSourceFetcher>();

        return services;
    }

    /// <summary>
    ///     Registers the URI-shape inference pipeline that lets manifest
    ///     entries use <c>{ "source": { "type": "uri", "uri": "..." } }</c>
    ///     and have the concrete kind (<c>github</c>, <c>azdo</c>,
    ///     <c>local</c>, ...) chosen automatically at load time.
    /// </summary>
    public static IServiceCollection AddSkillSourceInference(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Order matters: local takes precedence so a './something' that
        // happens to contain 'github.com' (unlikely but possible) is still
        // resolved as a local path.
        services.AddSingleton<ISkillSourceInferrer, LocalDirectorySkillSourceInferrer>();
        services.AddSingleton<ISkillSourceInferrer, GitHubSkillSourceInferrer>();
        services.AddSingleton<ISkillSourceInferrer, AzdoSkillSourceInferrer>();
        services.AddSingleton<SkillSourceInferenceCoordinator>();

        return services;
    }
}
