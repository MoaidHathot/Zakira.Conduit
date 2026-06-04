using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Mirroring;

namespace Zakira.Conduit.Core.UnitTests.Mirroring;

public sealed class AtomicDirectoryMirrorTests
{
    private static readonly string[] MdGlob = { "**/*.md" };
    private static readonly string[] TestExcludes = { "**/*.test.cs", "test/**" };
    private static readonly string[] DropBGlob = { "b.md" };

    [Fact]
    public async Task Mirrors_into_a_new_target_directory()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "b.txt"), "beta");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var count = await mirror.MirrorAsync(source, target);

        count.Should().Be(2);
        File.ReadAllText(Path.Combine(target, "a.txt")).Should().Be("alpha");
        File.ReadAllText(Path.Combine(target, "sub", "b.txt")).Should().Be("beta");
    }

    [Fact]
    public async Task Replaces_existing_target_atomically_and_removes_stale_files()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "stale.txt"), "old content");
        await File.WriteAllTextAsync(Path.Combine(source, "fresh.txt"), "new content");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        await mirror.MirrorAsync(source, target);

        File.Exists(Path.Combine(target, "stale.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(target, "fresh.txt")).Should().Be("new content");
    }

    [Fact]
    public async Task Leaves_no_staging_directories_after_success()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("targets", "entry");

        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "x");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        await mirror.MirrorAsync(source, target);

        var parent = Path.GetDirectoryName(target)!;
        Directory.EnumerateDirectories(parent)
            .Should().OnlyContain(d => Path.GetFileName(d) == "entry");
    }

    [Fact]
    public async Task Missing_source_throws()
    {
        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        using var tmp = new TempDir();

        var act = () => mirror.MirrorAsync(tmp.Combine("nope"), tmp.Combine("target"));
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Include_only_filter_keeps_only_matching_files()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "keep.md"), "k");
        await File.WriteAllTextAsync(Path.Combine(source, "drop.txt"), "d");
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "nested.md"), "n");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var filter = new MirrorFilter(includes: MdGlob, excludes: null);
        var count = await mirror.MirrorAsync(source, target, filter);

        count.Should().Be(2);
        File.Exists(Path.Combine(target, "keep.md")).Should().BeTrue();
        File.Exists(Path.Combine(target, "drop.txt")).Should().BeFalse();
        File.Exists(Path.Combine(target, "sub", "nested.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Exclude_filter_drops_matching_files()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "test"));
        await File.WriteAllTextAsync(Path.Combine(source, "code.cs"), "x");
        await File.WriteAllTextAsync(Path.Combine(source, "code.test.cs"), "y");
        await File.WriteAllTextAsync(Path.Combine(source, "test", "more.cs"), "z");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var filter = new MirrorFilter(includes: null, excludes: TestExcludes);
        var count = await mirror.MirrorAsync(source, target, filter);

        count.Should().Be(1);
        File.Exists(Path.Combine(target, "code.cs")).Should().BeTrue();
        File.Exists(Path.Combine(target, "code.test.cs")).Should().BeFalse();
        Directory.Exists(Path.Combine(target, "test")).Should().BeFalse();
    }

    [Fact]
    public async Task Exclude_takes_precedence_over_include()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(source, "b.md"), "b");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var filter = new MirrorFilter(includes: MdGlob, excludes: DropBGlob);
        var count = await mirror.MirrorAsync(source, target, filter);

        count.Should().Be(1);
        File.Exists(Path.Combine(target, "a.md")).Should().BeTrue();
        File.Exists(Path.Combine(target, "b.md")).Should().BeFalse();
    }

    [Fact]
    public async Task Filter_omits_empty_subdirectories()
    {
        using var tmp = new TempDir();
        var source = tmp.Combine("source");
        var target = tmp.Combine("target", "entry");

        Directory.CreateDirectory(Path.Combine(source, "kept"));
        Directory.CreateDirectory(Path.Combine(source, "dropped"));
        await File.WriteAllTextAsync(Path.Combine(source, "kept", "x.md"), "x");
        await File.WriteAllTextAsync(Path.Combine(source, "dropped", "y.txt"), "y");

        var mirror = new AtomicDirectoryMirror(NullLogger<AtomicDirectoryMirror>.Instance);
        var filter = new MirrorFilter(includes: MdGlob, excludes: null);
        await mirror.MirrorAsync(source, target, filter);

        Directory.Exists(Path.Combine(target, "kept")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "dropped")).Should().BeFalse();
    }
}
