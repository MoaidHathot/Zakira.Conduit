using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources;

namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     A pluggable <see cref="ISkillSourceFetcher"/> for tests. The fetcher
///     writes a set of files (provided by the caller) into a temp directory
///     and returns a <see cref="FetchedSource"/> that points at it.
/// </summary>
internal sealed class FakeFetcher : ISkillSourceFetcher
{
    public string SourceKind { get; }

    public Func<ISkillSource, IReadOnlyDictionary<string, string>> ContentProvider { get; set; }

    public int FetchCount { get; private set; }

    public FakeFetcher(string sourceKind = "github")
    {
        SourceKind = sourceKind;
        ContentProvider = _ => new Dictionary<string, string> { ["SKILL.md"] = "fake" };
    }

    public Task<FetchedSource> FetchAsync(ISkillSource source, FetchContext context, CancellationToken cancellationToken = default)
    {
        FetchCount++;

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
            resolvedRef: source is GitHubSkillSource gh ? gh.ResolvedRef ?? "<default>" : null,
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
