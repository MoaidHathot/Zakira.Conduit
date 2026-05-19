using Microsoft.Extensions.Logging.Abstractions;
using Zakira.Conduit.Core.UnitTests.TestHelpers;
using Zakira.Conduit.Mirroring;

namespace Zakira.Conduit.Core.UnitTests.Mirroring;

public sealed class AtomicDirectoryMirrorTests
{
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
}
