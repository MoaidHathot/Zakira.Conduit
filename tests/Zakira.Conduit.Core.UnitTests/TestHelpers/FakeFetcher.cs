using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     A pluggable <see cref="ISourceFetcher"/> for tests. The fetcher
///     writes a set of files (provided by the caller) into a temp directory
///     and returns a <see cref="FetchedSource"/> that points at it.
/// </summary>
internal sealed class FakeFetcher : ISourceFetcher
{
    public string SourceKind { get; }

    public Func<ISource, IReadOnlyDictionary<string, string>> ContentProvider { get; set; }

    private int _fetchCount;
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public FakeFetcher(string sourceKind = "github")
    {
        SourceKind = sourceKind;
        ContentProvider = _ => new Dictionary<string, string> { ["SKILL.md"] = "fake" };
    }

    public Task<FetchedSource> FetchAsync(ISource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _fetchCount);

        var dir = Path.Combine(Path.GetTempPath(), "conduit-fake-fetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        foreach (var (rel, content) in ContentProvider(source))
        {
            var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return Task.FromResult(FetchedSource.FromSingleDirectory(
            contentDirectory: dir,
            source: source,
            resolvedRef: source is GitHubSource gh ? gh.ResolvedRef ?? "<default>" : null,
            cleanup: () =>
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // best-effort
                }

                return ValueTask.CompletedTask;
            }));
    }
}
