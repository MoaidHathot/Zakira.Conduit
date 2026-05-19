using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Zakira.Conduit.Hosting;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Mirroring;
using Zakira.Conduit.Paths;
using Zakira.Conduit.Sources;
using Zakira.Conduit.Sources.GitHub;
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
        services.TryAddSingleton<IManifestLoader, JsonManifestLoader>();
        services.TryAddSingleton<IPathResolver, DefaultPathResolver>();
        services.TryAddSingleton<IDirectoryMirror, AtomicDirectoryMirror>();
        services.TryAddSingleton<ISkillSourceFetcherRegistry, DefaultSkillSourceFetcherRegistry>();
        services.TryAddSingleton<IConduitSynchronizer, DefaultConduitSynchronizer>();

        services.AddGitHubSkillSource(configureGitHub);
        services.AddLocalDirectorySkillSource();

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

        services.AddSingleton<ISkillSourceFetcher, GitHubSkillSourceFetcher>();

        return services;
    }
}
