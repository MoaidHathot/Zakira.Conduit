using System.Collections.Concurrent;
using Zakira.Conduit.Hosting;

namespace Zakira.Conduit.Core.UnitTests.TestHelpers;

/// <summary>
///     In-memory <see cref="IEnvironment"/> for tests.
/// </summary>
internal sealed class FakeEnvironment : IEnvironment
{
    private readonly ConcurrentDictionary<string, string> _vars = new();

    public string HomeDirectory { get; set; } = OperatingSystem.IsWindows() ? @"C:\Users\fake" : "/home/fake";

    public string CurrentDirectory { get; set; } = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    public bool IsWindows { get; set; } = OperatingSystem.IsWindows();

    public FakeEnvironment Set(string key, string? value)
    {
        if (value is null)
        {
            _vars.TryRemove(key, out _);
        }
        else
        {
            _vars[key] = value;
        }

        return this;
    }

    public string? GetEnvironmentVariable(string name) =>
        _vars.TryGetValue(name, out var v) ? v : null;

    public string GetHomeDirectory() => HomeDirectory;

    public string GetCurrentDirectory() => CurrentDirectory;
}
