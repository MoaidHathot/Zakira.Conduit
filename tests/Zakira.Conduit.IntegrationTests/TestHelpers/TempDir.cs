namespace Zakira.Conduit.IntegrationTests.TestHelpers;

/// <summary>
///     Scratch directory that wipes itself on dispose. Same shape as the unit-test
///     helper but lives in this assembly to keep test projects independent.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? prefix = null)
    {
        var name = (prefix ?? "conduit-itests") + "-" + Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
