# Zakira.Conduit.Core

The library that powers the [`Zakira.Conduit`](https://www.nuget.org/packages/Zakira.Conduit/) global tool.

`Zakira.Conduit.Core` exposes the manifest model, source abstractions, mirror engine, and synchronizer that the CLI tool wraps. Use it directly when you want to embed conduit's "mirror skills from many sources to many local folders" capability inside your own .NET application instead of shelling out to the tool.

## Install

```bash
dotnet add package Zakira.Conduit.Core
```

## Quickstart

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.DependencyInjection;
using Zakira.Conduit.Manifest;
using Zakira.Conduit.Synchronization;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddConduitCore(github => { github.Token = "ghp_..."; }); // optional

await using var provider = services.BuildServiceProvider();
var loader        = provider.GetRequiredService<IManifestLoader>();
var synchronizer  = provider.GetRequiredService<IConduitSynchronizer>();

var manifestPath = "/path/to/conduit.json";
var manifest     = await loader.LoadAsync(manifestPath);
var report       = await synchronizer.SyncAsync(manifest, manifestPath, new SyncOptions());

Console.WriteLine(report.Succeeded ? "ok" : "had failures");
```

## What's in the box

| Namespace | Public types |
|---|---|
| `Zakira.Conduit.Manifest` | `ConduitManifest`, `ConduitEntry`, `ISkillSource`, `GitHubSkillSource`, `LocalDirectorySkillSource`, `IManifestLoader`, `IManifestLocator`, `ManifestValidator`, `ManifestException` |
| `Zakira.Conduit.Sources` | `ISkillSourceFetcher`, `ISkillSourceFetcherRegistry`, `FetchedSource`, `FetchedContent`, `FetchContext` |
| `Zakira.Conduit.Sources.GitHub` | `GitHubSkillSourceFetcher`, `IGitHubArchiveDownloader`, `GitHubFetcherOptions`, `GitHubDownloadException` |
| `Zakira.Conduit.Sources.Local` | `LocalDirectorySkillSourceFetcher`, `LocalSourceNotFoundException` |
| `Zakira.Conduit.Synchronization` | `IConduitSynchronizer`, `SyncOptions`, `SyncReport`, `SyncEntryResult`, `SyncTargetResult` |
| `Zakira.Conduit.Mirroring` | `IDirectoryMirror`, `AtomicDirectoryMirror` |
| `Zakira.Conduit.Paths` | `IPathResolver`, `DefaultPathResolver` |
| `Zakira.Conduit.Hosting` | `IEnvironment`, `SystemEnvironment` |
| `Zakira.Conduit.DependencyInjection` | `ConduitCoreServiceCollectionExtensions` |

## Adding a new source kind

Implement two small types and register them with DI:

```csharp
public sealed record GitLabSkillSource : ISkillSource
{
    public const string TypeDiscriminator = "gitlab";
    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("ref")]     public string?         Ref     { get; init; }
    [JsonIgnore] public string Kind => TypeDiscriminator;
}

public sealed class GitLabSkillSourceFetcher : ISkillSourceFetcher
{
    public string SourceKind => GitLabSkillSource.TypeDiscriminator;

    public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken ct = default)
    {
        // ... download zip, extract to a temp dir, return a FetchedSource ...
    }
}
```

Then register the discriminator on `ISkillSource` (`[JsonDerivedType]`) and the fetcher with DI:

```csharp
services.AddSingleton<ISkillSourceFetcher, GitLabSkillSourceFetcher>();
```

The synchronizer and mirror are source-agnostic, so no further wiring is needed.

## See also

- The CLI tool: [`Zakira.Conduit`](https://www.nuget.org/packages/Zakira.Conduit/).
- The repository, including manifest schema and examples: <https://github.com/MoaidHathot/Zakira.Conduit>.

## License

MIT.
