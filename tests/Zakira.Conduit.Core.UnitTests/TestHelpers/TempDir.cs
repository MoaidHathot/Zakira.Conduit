namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     Creates a scratch directory under the system temp folder that is wiped
///     on dispose. Used by tests that touch the real filesystem.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? prefix = null)
    {
        var name = (prefix ?? "conduit-tests") + "-" + Guid.NewGuid().ToString("N");
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
