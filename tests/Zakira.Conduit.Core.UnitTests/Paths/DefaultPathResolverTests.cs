using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Paths;

namespace Zakira.Conduit.Core.UnitTests.Paths;

public sealed class DefaultPathResolverTests
{
    private static readonly bool RunningOnWindows = OperatingSystem.IsWindows();

    private static (DefaultPathResolver Resolver, FakeEnvironment Env) Build()
    {
        // Pin the fake env to the host platform so generated paths are valid.
        var env = new FakeEnvironment
        {
            HomeDirectory = RunningOnWindows ? @"C:\Users\me" : "/home/me",
            CurrentDirectory = RunningOnWindows ? @"C:\work" : "/work",
            IsWindows = RunningOnWindows,
        };
        return (new DefaultPathResolver(env), env);
    }

    [Fact]
    public void Tilde_alone_expands_to_home()
    {
        var (resolver, env) = Build();

        resolver.Resolve("~", env.CurrentDirectory).Should().Be(Path.GetFullPath(env.HomeDirectory));
    }

    [Fact]
    public void Tilde_slash_expands_to_home()
    {
        var (resolver, env) = Build();

        resolver.Resolve("~/skills/foo", env.CurrentDirectory)
            .Should().Be(Path.GetFullPath(Path.Combine(env.HomeDirectory, "skills", "foo")));
    }

    [Fact]
    public void Tilde_inside_path_is_left_alone()
    {
        var (resolver, env) = Build();

        var resolved = resolver.Resolve("foo/~bar", env.CurrentDirectory);
        resolved.Should().Be(Path.GetFullPath(Path.Combine(env.CurrentDirectory, "foo", "~bar")));
    }

    [Fact]
    public void Dollar_var_is_expanded()
    {
        var (resolver, env) = Build();
        var expanded = RunningOnWindows ? @"C:\expanded" : "/expanded";
        env.Set("MYVAR", expanded);

        resolver.Resolve("$MYVAR/sub", env.CurrentDirectory)
            .Should().Be(Path.GetFullPath(Path.Combine(expanded, "sub")));
    }

    [Fact]
    public void Dollar_brace_var_is_expanded()
    {
        var (resolver, env) = Build();
        var expanded = RunningOnWindows ? @"C:\expanded" : "/expanded";
        env.Set("MYVAR", expanded);

        resolver.Resolve("${MYVAR}/sub", env.CurrentDirectory)
            .Should().Be(Path.GetFullPath(Path.Combine(expanded, "sub")));
    }

    [Fact]
    public void Unknown_dollar_var_is_left_literal()
    {
        var (resolver, env) = Build();

        var resolved = resolver.Resolve("$NOPE/x", env.CurrentDirectory);
        resolved.Should().Be(Path.GetFullPath(Path.Combine(env.CurrentDirectory, "$NOPE", "x")));
    }

    [Fact]
    public void Absolute_path_is_returned_unchanged()
    {
        var (resolver, env) = Build();
        var input = RunningOnWindows ? @"C:\absolute\path" : "/absolute/path";

        resolver.Resolve(input, env.CurrentDirectory).Should().Be(Path.GetFullPath(input));
    }

    [Fact]
    public void Relative_path_is_rooted_at_base()
    {
        var (resolver, env) = Build();

        resolver.Resolve("relative/sub", env.CurrentDirectory)
            .Should().Be(Path.GetFullPath(Path.Combine(env.CurrentDirectory, "relative", "sub")));
    }

    [Fact]
    public void Empty_input_throws()
    {
        var (resolver, env) = Build();
        var act = () => resolver.Resolve(string.Empty, env.CurrentDirectory);
        act.Should().Throw<ArgumentException>();
    }
}
