using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Manifest;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class DefaultManifestLocatorTests
{
    [Fact]
    public void Explicit_path_takes_precedence()
    {
        var env = new FakeEnvironment().Set("XDG_CONFIG_HOME", "/xdg");
        var locator = new DefaultManifestLocator(env);

        var explicitPath = OperatingSystem.IsWindows() ? @"C:\custom\manifest.json" : "/custom/manifest.json";
        var candidates = locator.EnumerateCandidates(explicitPath);

        candidates.Should().HaveCount(1);
        candidates[0].Should().Be(explicitPath);
    }

    [Fact]
    public void Relative_explicit_path_is_rooted_against_cwd()
    {
        var env = new FakeEnvironment { CurrentDirectory = OperatingSystem.IsWindows() ? @"C:\work" : "/work" };
        var locator = new DefaultManifestLocator(env);

        var candidates = locator.EnumerateCandidates("subdir/foo.json");

        candidates.Should().HaveCount(1);
        candidates[0].Should().Be(Path.GetFullPath(Path.Combine(env.CurrentDirectory, "subdir/foo.json")));
    }

    [Fact]
    public void Xdg_config_home_is_searched_first()
    {
        var env = new FakeEnvironment().Set("XDG_CONFIG_HOME", OperatingSystem.IsWindows() ? @"C:\xdg" : "/xdg");
        var locator = new DefaultManifestLocator(env);

        var candidates = locator.EnumerateCandidates(explicitPath: null);

        candidates[0].Should().Be(Path.Combine(env.GetEnvironmentVariable("XDG_CONFIG_HOME")!, "Zakira.Conduit", "conduit.json"));
    }

    [Fact]
    public void Falls_back_to_home_dot_config_when_no_xdg()
    {
        var env = new FakeEnvironment { HomeDirectory = OperatingSystem.IsWindows() ? @"C:\Users\me" : "/home/me", IsWindows = false };
        env.Set("XDG_CONFIG_HOME", null);
        var locator = new DefaultManifestLocator(env);

        var candidates = locator.EnumerateCandidates(explicitPath: null);

        candidates.Should().Contain(Path.Combine(env.HomeDirectory, ".config", "Zakira.Conduit", "conduit.json"));
    }

    [Fact]
    public void Appdata_is_never_included_even_on_windows()
    {
        // We deliberately follow XDG everywhere and do NOT probe %APPDATA%
        // so the discovery rules are identical across OSes.
        var env = new FakeEnvironment { IsWindows = true };
        env.Set("APPDATA", @"C:\Users\me\AppData\Roaming");
        var locator = new DefaultManifestLocator(env);

        var candidates = locator.EnumerateCandidates(explicitPath: null);

        candidates.Should().NotContain(c => c.Contains("AppData", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cwd_fallback_is_last()
    {
        var env = new FakeEnvironment { CurrentDirectory = OperatingSystem.IsWindows() ? @"C:\work" : "/work" };
        var locator = new DefaultManifestLocator(env);

        var candidates = locator.EnumerateCandidates(explicitPath: null);

        candidates[^1].Should().Be(Path.Combine(env.CurrentDirectory, "conduit.json"));
    }

    [Fact]
    public void Locate_throws_when_no_candidate_exists()
    {
        using var tmp = new TempDir();
        var env = new FakeEnvironment
        {
            CurrentDirectory = tmp.Path,
            HomeDirectory = tmp.Path,
            IsWindows = false,
        };

        var locator = new DefaultManifestLocator(env);

        var act = () => locator.Locate(explicitPath: null);
        act.Should().Throw<ManifestException>().WithMessage("*No conduit manifest found*");
    }

    [Fact]
    public void Locate_returns_first_existing_candidate()
    {
        using var tmp = new TempDir();
        var configDir = Path.Combine(tmp.Path, ".config", "Zakira.Conduit");
        Directory.CreateDirectory(configDir);
        var manifestPath = Path.Combine(configDir, "conduit.json");
        File.WriteAllText(manifestPath, "{}");

        var env = new FakeEnvironment
        {
            CurrentDirectory = tmp.Path,
            HomeDirectory = tmp.Path,
            IsWindows = false,
        };

        var locator = new DefaultManifestLocator(env);
        var resolved = locator.Locate(explicitPath: null);

        resolved.Should().Be(Path.GetFullPath(manifestPath));
    }
}
