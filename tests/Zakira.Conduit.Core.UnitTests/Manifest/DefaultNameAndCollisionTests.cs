using Zakira.Conduit.Manifest;
using Zakira.Conduit.Sources.Inference;

namespace Zakira.Conduit.Core.UnitTests.Manifest;

public sealed class DefaultNameAndCollisionTests
{
    private static readonly string[] BundleChildrenStandalone = { "ActionView", "PowerReview", "McpLense" };
    private static readonly string[] BundleChildrenWithPrefix = { "bundle-ActionView", "bundle-PowerReview" };
    private static readonly string[] ChildrenWithArrowAlias = { "CustomAV", "PowerReview" };
    private static readonly string[] ChildrenWithWrapperAlias = { "ActVw", "PowerReview" };

    private static SkillSourceInferenceCoordinator BuildCoordinator() =>
        new(new ISkillSourceInferrer[]
        {
            new LocalDirectorySkillSourceInferrer(),
            new GitHubSkillSourceInferrer(),
            new AzdoSkillSourceInferrer(),
        });

    private static ConduitManifest Rewrite(string json) =>
        BuildCoordinator().Rewrite(
            System.Text.Json.JsonSerializer.Deserialize<ConduitManifest>(json, ManifestJson.ReadOptions)
            ?? throw new InvalidOperationException("manifest null"));

    // --- default-name derivation per source kind ---

    [Fact]
    public void Github_default_name_is_repo_name_not_subpath_basename()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "https://github.com/MoaidHathot/ActionView/tree/main/skills",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries[0].Name.Should().Be("ActionView");
        var gh = manifest.Entries[0].Source.Should().BeOfType<GitHubSkillSource>().Subject;
        gh.Path.Should().Be("skills");
        gh.Branch.Should().Be("main");
    }

    [Fact]
    public void Local_default_name_is_directory_basename()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "./my-local-skill",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries[0].Name.Should().Be("my-local-skill");
        manifest.Entries[0].Source.Should().BeOfType<LocalDirectorySkillSource>();
    }

    [Fact]
    public void Azdo_default_name_is_repo_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "https://dev.azure.com/contoso/Conduit/_git/agent-skills",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries[0].Name.Should().Be("agent-skills");
        manifest.Entries[0].Source.Should().BeOfType<AzdoSkillSource>();
    }

    [Fact]
    public void Explicit_name_wins_over_source_derived_default()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "MyOverride",
                  "source": "https://github.com/MoaidHathot/ActionView",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries[0].Name.Should().Be("MyOverride");
    }

    // --- array expansion with default names ---

    [Fact]
    public void Array_elements_get_independent_source_derived_names_when_parent_has_no_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": [
                    "https://github.com/MoaidHathot/ActionView/tree/main/skills",
                    "https://github.com/MoaidHathot/PowerReview/tree/main/skills",
                    "https://github.com/MoaidHathot/McpLense/tree/main/skills"
                  ],
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries.Should().HaveCount(3);
        manifest.Entries.Select(e => e.Name).Should().BeEquivalentTo(BundleChildrenStandalone);
    }

    [Fact]
    public void Array_elements_get_parent_prefix_when_parent_has_explicit_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "bundle",
                  "source": [
                    "https://github.com/MoaidHathot/ActionView/tree/main/skills",
                    "https://github.com/MoaidHathot/PowerReview/tree/main/skills"
                  ],
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries.Should().HaveCount(2);
        manifest.Entries.Select(e => e.Name).Should().BeEquivalentTo(BundleChildrenWithPrefix);
    }

    [Fact]
    public void Array_element_arrow_alias_overrides_source_derived_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": [
                    "https://github.com/MoaidHathot/ActionView/tree/main/skills -> CustomAV",
                    "https://github.com/MoaidHathot/PowerReview/tree/main/skills"
                  ],
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries.Select(e => e.Name).Should().BeEquivalentTo(ChildrenWithArrowAlias);
    }

    [Fact]
    public void Array_element_wrapper_object_overrides_source_derived_name()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": [
                    { "source": "https://github.com/MoaidHathot/ActionView/tree/main/skills", "as": "ActVw" },
                    "https://github.com/MoaidHathot/PowerReview/tree/main/skills"
                  ],
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        manifest.Entries.Select(e => e.Name).Should().BeEquivalentTo(ChildrenWithWrapperAlias);
    }

    // --- cross-entry destination collision detection ---

    [Fact]
    public void Two_entries_with_same_target_and_name_are_rejected_by_validator()
    {
        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "shared",
                    Source = new GitHubSkillSource { Repo = "owner/repo1" },
                    Targets = [new PathSpec("./out")],
                },
                new ConduitEntry
                {
                    Name = "shared",
                    Source = new GitHubSkillSource { Repo = "owner/repo2" },
                    Targets = [new PathSpec("./out")],
                },
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().Contain(e => e.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_entries_with_same_target_and_different_names_do_not_collide()
    {
        const string json = """
            {
              "entries": [
                {
                  "source": "https://github.com/MoaidHathot/ActionView/tree/main/skills",
                  "targets": [ "./out" ]
                },
                {
                  "source": "https://github.com/MoaidHathot/PowerReview/tree/main/skills",
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var manifest = Rewrite(json);
        var errors = ManifestValidator.Validate(manifest);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Cross_entry_destination_collision_via_alias_is_reported()
    {
        // Two distinct entry names, but the per-target 'as' alias makes them
        // collide at <target>/Shared/.
        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "first",
                    Source = new GitHubSkillSource { Repo = "owner/repo1" },
                    Targets = [new PathSpec("./out", "Shared")],
                },
                new ConduitEntry
                {
                    Name = "second",
                    Source = new GitHubSkillSource { Repo = "owner/repo2" },
                    Targets = [new PathSpec("./out", "Shared")],
                },
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().Contain(e => e.Contains("./out/Shared", StringComparison.Ordinal) || e.Contains(@".\out/Shared", StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains("first", StringComparison.Ordinal));
    }

    [Fact]
    public void Cross_entry_destination_collision_normalises_trailing_slash_and_backslash()
    {
        var manifest = new ConduitManifest
        {
            Version = 1,
            Entries =
            [
                new ConduitEntry
                {
                    Name = "first",
                    Source = new GitHubSkillSource { Repo = "owner/repo1" },
                    Targets = [new PathSpec(@".\out\")],
                },
                new ConduitEntry
                {
                    Name = "second",
                    Source = new GitHubSkillSource { Repo = "owner/repo2" },
                    Targets = [new PathSpec("./out", "first")],
                },
            ],
        };

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().Contain(e => e.Contains("first", StringComparison.Ordinal));
    }

    [Fact]
    public void Unnamed_entry_with_unsupported_source_kind_triggers_friendly_error()
    {
        // Multi-path local has no defaultable name. Validator should produce
        // the friendly "set 'name' or supply an alias" message.
        const string json = """
            {
              "entries": [
                {
                  "source": { "type": "local", "paths": [ "./a", "./b" ] },
                  "targets": [ "./out" ]
                }
              ]
            }
            """;

        var act = () => Rewrite(json);
        // Rewrite itself doesn't throw; ConduitEntry.Name stays null and the
        // validator surfaces the friendly message.
        var manifest = act();
        manifest.Entries[0].Name.Should().BeNull();

        var errors = ManifestValidator.Validate(manifest);
        errors.Should().Contain(e => e.Contains("could not be determined", StringComparison.Ordinal));
    }
}
