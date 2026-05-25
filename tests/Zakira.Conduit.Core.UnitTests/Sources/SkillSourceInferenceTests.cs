using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Core.UnitTests.Sources;

public sealed class SkillSourceInferenceTests
{
    private static readonly string[] AzAuth = { "az" };

    private static SkillSourceInferenceCoordinator BuildCoordinator() =>
        new(new ISkillSourceInferrer[]
        {
            new LocalDirectorySkillSourceInferrer(),
            new GitHubSkillSourceInferrer(),
            new AzdoSkillSourceInferrer(),
        });

    // --- LocalDirectorySkillSourceInferrer ---

    [Theory]
    [InlineData("./foo")]
    [InlineData(".\\foo")]
    [InlineData("../foo/bar")]
    [InlineData("..\\foo\\bar")]
    [InlineData("/etc/skills")]
    [InlineData("~/skills")]
    [InlineData("~user/skills")]
    [InlineData("$HOME/skills")]
    [InlineData("${HOME}/skills")]
    [InlineData("%APPDATA%/skills")]
    [InlineData("C:\\skills")]
    [InlineData("D:/skills")]
    public void Local_inferrer_recognises_path_shapes(string uri)
    {
        var inf = new LocalDirectorySkillSourceInferrer();
        inf.CanHandle(uri).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://github.com/o/r")]
    [InlineData("owner/repo")]
    [InlineData("")]
    [InlineData("   ")]
    public void Local_inferrer_rejects_non_paths(string uri)
    {
        var inf = new LocalDirectorySkillSourceInferrer();
        inf.CanHandle(uri).Should().BeFalse();
    }

    [Fact]
    public void Local_inferrer_rejects_irrelevant_fields()
    {
        var inf = new LocalDirectorySkillSourceInferrer();
        var act = () => inf.Infer(new UriBasedSkillSource { Uri = "./skills", Branch = "main" });
        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*branch*");
    }

    // --- GitHubSkillSourceInferrer ---

    [Theory]
    [InlineData("https://github.com/anthropics/skills")]
    [InlineData("http://github.com/anthropics/skills.git")]
    [InlineData("github.com/anthropics/skills")]
    [InlineData("git@github.com:anthropics/skills.git")]
    [InlineData("ssh://git@github.com/anthropics/skills")]
    public void GitHub_inferrer_recognises_github_urls(string uri)
    {
        var inf = new GitHubSkillSourceInferrer();
        inf.CanHandle(uri).Should().BeTrue();
    }

    [Theory]
    [InlineData("owner/repo")]          // bare slug rejected
    [InlineData("./skills")]
    [InlineData("https://gitlab.com/g/r")]
    [InlineData("https://dev.azure.com/o/p/_git/r")]
    public void GitHub_inferrer_rejects_non_github_or_slug(string uri)
    {
        var inf = new GitHubSkillSourceInferrer();
        inf.CanHandle(uri).Should().BeFalse();
    }

    [Fact]
    public void GitHub_inferrer_copies_path_branch_commit()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://github.com/anthropics/skills",
            Path = "code-review",
            Branch = "main",
        });

        inferred.Should().BeOfType<GitHubSkillSource>();
        var gh = (GitHubSkillSource)inferred;
        gh.Repo.Should().Be("anthropics/skills");
        gh.Path.Should().Be("code-review");
        gh.Branch.Should().Be("main");
    }

    [Fact]
    public void GitHub_inferrer_rejects_azdo_only_fields()
    {
        var inf = new GitHubSkillSourceInferrer();
        var act = () => inf.Infer(new UriBasedSkillSource
        {
            Uri = "https://github.com/o/r",
            Tag = "v1",
        });
        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*tag*");
    }

    // --- AzdoSkillSourceInferrer ---

    [Theory]
    [InlineData("https://dev.azure.com/contoso/Conduit/_git/agent-skills")]
    [InlineData("https://contoso.visualstudio.com/Conduit/_git/agent-skills")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/Conduit/agent-skills")]
    [InlineData("https://devops.contoso.internal/Collection/Project/_git/repo")]
    public void Azdo_inferrer_recognises_azdo_urls(string uri)
    {
        var inf = new AzdoSkillSourceInferrer();
        inf.CanHandle(uri).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://github.com/o/r")]
    [InlineData("./skills")]
    [InlineData("https://example.com/no/git/marker")]
    public void Azdo_inferrer_rejects_non_azdo(string uri)
    {
        var inf = new AzdoSkillSourceInferrer();
        inf.CanHandle(uri).Should().BeFalse();
    }

    [Fact]
    public void Azdo_inferrer_copies_overrides()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
            Branch = "main",
            Path = "skills/x",
            Auth = AzAuth,
        });

        inferred.Should().BeOfType<AzdoSkillSource>();
        var azdo = (AzdoSkillSource)inferred;
        azdo.Organization.Should().Be("contoso");
        azdo.Project.Should().Be("Conduit");
        azdo.Repo.Should().Be("agent-skills");
        azdo.Branch.Should().Be("main");
        azdo.Path.Should().Be("skills/x");
        azdo.Auth.Should().BeEquivalentTo(AzAuth);
    }

    // --- Coordinator ---

    [Fact]
    public void Coordinator_throws_on_unrecognised_uri()
    {
        var coord = BuildCoordinator();
        var act = () => coord.Infer(new UriBasedSkillSource { Uri = "owner/repo" });
        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*owner/repo*");
    }

    [Fact]
    public void Coordinator_throws_on_empty_uri()
    {
        var coord = BuildCoordinator();
        var act = () => coord.Infer(new UriBasedSkillSource { Uri = "" });
        act.Should().Throw<SkillSourceInferenceException>();
    }

    [Fact]
    public void Rewrite_preserves_concrete_sources()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "explicit",
                    Source = new GitHubSkillSource { Repo = "owner/repo" },
                    Targets = ["./out"],
                },
                new ConduitEntry
                {
                    Name = "inferred",
                    Source = new UriBasedSkillSource { Uri = "https://github.com/o/r", Branch = "main" },
                    Targets = ["./out"],
                },
            ],
        };

        var rewritten = coord.Rewrite(manifest);
        rewritten.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
        rewritten.Entries[1].Source.Should().BeOfType<GitHubSkillSource>();
        ((GitHubSkillSource)rewritten.Entries[1].Source).Branch.Should().Be("main");
    }

    [Fact]
    public void Rewrite_decorates_inference_error_with_entry_context()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "broken",
                    Source = new UriBasedSkillSource { Uri = "not-a-url" },
                    Targets = ["./out"],
                },
            ],
        };

        var act = () => coord.Rewrite(manifest);
        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*broken*");
    }

    [Fact]
    public void Local_inference_round_trips_through_validator()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "local-uri",
                    Source = new UriBasedSkillSource { Uri = "./local-skill-sample" },
                    Targets = ["./out"],
                },
            ],
        };

        var rewritten = coord.Rewrite(manifest);
        ManifestValidator.Validate(rewritten).Should().BeEmpty();
        rewritten.Entries[0].Source.Should().BeOfType<LocalDirectorySkillSource>();
    }

    // --- URL sub-path inference ---

    [Fact]
    public void GitHub_inferrer_extracts_subpath_from_browse_url()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://github.com/anthropics/skills/code-review",
        });

        var gh = inferred.Should().BeOfType<GitHubSkillSource>().Subject;
        gh.Repo.Should().Be("anthropics/skills");
        gh.Path.Should().Be("code-review");
        gh.Branch.Should().BeNull();
    }

    [Fact]
    public void GitHub_inferrer_extracts_branch_and_subpath_from_tree_url()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://github.com/anthropics/skills/tree/main/code-review/sub",
        });

        var gh = inferred.Should().BeOfType<GitHubSkillSource>().Subject;
        gh.Repo.Should().Be("anthropics/skills");
        gh.Path.Should().Be("code-review/sub");
        gh.Branch.Should().Be("main");
    }

    [Fact]
    public void GitHub_inferrer_rejects_url_subpath_when_explicit_path_also_set()
    {
        var coord = BuildCoordinator();
        var act = () => coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://github.com/anthropics/skills/code-review",
            Path = "other",
        });

        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*sub-path*");
    }

    [Fact]
    public void Azdo_inferrer_extracts_subpath_from_browse_url()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://dev.azure.com/contoso/Conduit/_git/agent-skills/skills/code-review",
        });

        var azdo = inferred.Should().BeOfType<AzdoSkillSource>().Subject;
        azdo.Organization.Should().Be("contoso");
        azdo.Project.Should().Be("Conduit");
        azdo.Repo.Should().Be("agent-skills");
        azdo.Path.Should().Be("skills/code-review");
    }

    [Fact]
    public void Azdo_inferrer_extracts_branch_from_version_query_param()
    {
        var coord = BuildCoordinator();
        var inferred = coord.Infer(new UriBasedSkillSource
        {
            Uri = "https://dev.azure.com/contoso/Conduit/_git/agent-skills?version=GBmain&path=/skills",
        });

        var azdo = inferred.Should().BeOfType<AzdoSkillSource>().Subject;
        azdo.Branch.Should().Be("main");
        azdo.Path.Should().Be("skills");
    }

    // --- Array source ---

    [Fact]
    public void Array_source_expands_into_one_entry_per_element()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "bundle",
                    Source = new ArraySkillSource
                    {
                        Elements =
                        [
                            new UriBasedSkillSource { Uri = "https://github.com/foo/bar/skills" },
                            new UriBasedSkillSource { Uri = "https://github.com/foo/bar2/skills" },
                            new UriBasedSkillSource { Uri = "./local/skill" },
                        ],
                    },
                    Targets = ["~/.config/claude/skills"],
                },
            ],
        };

        var rewritten = coord.Rewrite(manifest);
        rewritten.Entries.Should().HaveCount(3);

        var names = rewritten.Entries.Select(e => e.Name).ToArray();
        names.Should().OnlyHaveUniqueItems();
        names.Should().AllSatisfy(n => n.Should().StartWith("bundle-"));

        rewritten.Entries[0].Source.Should().BeOfType<GitHubSkillSource>();
        rewritten.Entries[1].Source.Should().BeOfType<GitHubSkillSource>();
        rewritten.Entries[2].Source.Should().BeOfType<LocalDirectorySkillSource>();

        // Targets are duplicated onto each expanded entry.
        rewritten.Entries.Should().OnlyContain(e => e.Targets.Count == 1 && e.Targets[0].Path == "~/.config/claude/skills");
    }

    [Fact]
    public void Array_source_disambiguates_colliding_basenames()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "bundle",
                    Source = new ArraySkillSource
                    {
                        Elements =
                        [
                            new UriBasedSkillSource { Uri = "https://github.com/foo/bar/skills" },
                            new UriBasedSkillSource { Uri = "https://github.com/baz/qux/skills" },
                        ],
                    },
                    Targets = ["./out"],
                },
            ],
        };

        var rewritten = coord.Rewrite(manifest);
        rewritten.Entries.Should().HaveCount(2);
        rewritten.Entries.Select(e => e.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Array_source_rejects_per_target_aliases()
    {
        var coord = BuildCoordinator();
        var manifest = new ConduitManifest
        {
            Entries =
            [
                new ConduitEntry
                {
                    Name = "bundle",
                    Source = new ArraySkillSource
                    {
                        Elements = [new UriBasedSkillSource { Uri = "./a" }],
                    },
                    Targets = [new PathSpec("./out", "alias")],
                },
            ],
        };

        var act = () => coord.Rewrite(manifest);
        act.Should().Throw<SkillSourceInferenceException>().WithMessage("*as*alias*");
    }
}
