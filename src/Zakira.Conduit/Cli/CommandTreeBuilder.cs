using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Zakira.Conduit.Cli.Commands;

namespace Zakira.Conduit.Cli;

/// <summary>
///     Builds the <see cref="RootCommand"/> tree and wires each command's
///     action to a handler resolved from the DI container.
/// </summary>
internal static class CommandTreeBuilder
{
    public static RootCommand Build(IServiceProvider services)
    {
        var root = new RootCommand(
            "conduit \u2014 sync agent skills from remote sources (e.g. GitHub) into local agent directories.");

        // Shared options are added as recursive globals so subcommands inherit them.
        root.Options.Add(CommonOptions.Manifest);
        root.Options.Add(CommonOptions.Verbosity);
        root.Options.Add(CommonOptions.Verbose);
        root.Options.Add(CommonOptions.Quiet);
        root.Options.Add(CommonOptions.Output);

        CommonOptions.Manifest.Recursive = true;

        root.Subcommands.Add(BuildSyncCommand(services));
        root.Subcommands.Add(BuildListCommand(services));
        root.Subcommands.Add(BuildValidateCommand(services));
        root.Subcommands.Add(BuildInitCommand(services));
        root.Subcommands.Add(BuildPinOrUpdateCommand(services, "pin", "Lock every GitHub / AzDO entry to a specific commit SHA by rewriting its source to the URL-native pinned form (GitHub: tree/<sha>/<path>; AzDO: ?version=GC<sha>). Already-pinned entries are skipped; run 'conduit unpin' first to refresh."));
        root.Subcommands.Add(BuildPinOrUpdateCommand(services, "update", "Alias of 'pin'. Lock each entry to the latest commit on its tracked branch (or the repo's default branch when none is set)."));
        root.Subcommands.Add(BuildUnpinCommand(services));
        root.Subcommands.Add(BuildWatchCommand(services));
        root.Subcommands.Add(BuildStatusCommand(services));
        root.Subcommands.Add(BuildCleanCommand(services));
        root.Subcommands.Add(BuildCopyCommand(services));
        root.Subcommands.Add(BuildSkillsCommand(services));

        // Default action when the user runs `conduit` with no subcommand: show help.
        root.SetAction(parseResult =>
        {
            Console.Error.WriteLine(parseResult.CommandResult.Command.Description);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Run 'conduit --help' to see available commands.");
            return 1;
        });

        return root;
    }

    private static Command BuildSyncCommand(IServiceProvider services)
    {
        var entryOption = new Option<string[]>("--entry", "-e")
        {
            Description = "Sync only the named entry. Repeatable, and comma-separated values are accepted (e.g. '--entry a,b -e c'). When omitted, all enabled entries are synced.",
            AllowMultipleArgumentsPerToken = true,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Fetch sources and report what would change, without writing to any target.",
        };

        var stopOnFirstErrorOption = new Option<bool>("--stop-on-first-error")
        {
            Description = "Abort the run on the first failing entry instead of attempting subsequent ones.",
        };

        var forceOption = new Option<bool>("--force", "-f")
        {
            Description = "Ignore the cached state file and re-fetch/re-mirror every entry, even if it appears up-to-date.",
        };

        var parallelOption = new Option<int>("--parallel", "-p")
        {
            Description = "Maximum number of entries to sync in parallel. Default: 4. Use 1 to force sequential execution.",
            DefaultValueFactory = _ => 4,
        };

        var pruneOption = new Option<bool>("--prune")
        {
            Description = "After a successful sync, run the orphan cleaner: remove destination directories whose owning entry has been deleted from the manifest. Requires --prune-yes (or --yes) for non-interactive runs.",
        };

        var pruneYesOption = new Option<bool>("--prune-yes")
        {
            Description = "Skip the cleanup confirmation prompt that --prune would otherwise show. Required for --prune in non-interactive sessions.",
        };

        var command = new Command("sync", "Synchronize manifest entries into their target directories.");
        command.Options.Add(entryOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(stopOnFirstErrorOption);
        command.Options.Add(forceOption);
        command.Options.Add(parallelOption);
        command.Options.Add(pruneOption);
        command.Options.Add(pruneYesOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<SyncCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                entries: EntryFilter.Normalise(parseResult.GetValue(entryOption)),
                dryRun: parseResult.GetValue(dryRunOption),
                stopOnFirstError: parseResult.GetValue(stopOnFirstErrorOption),
                force: parseResult.GetValue(forceOption),
                maxParallelism: parseResult.GetValue(parallelOption),
                prune: parseResult.GetValue(pruneOption),
                pruneYes: parseResult.GetValue(pruneYesOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildListCommand(IServiceProvider services)
    {
        var command = new Command("list", "List entries in the manifest.");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<ListCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildValidateCommand(IServiceProvider services)
    {
        var command = new Command("validate", "Validate the manifest without performing any IO against remote sources.");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<ValidateCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildInitCommand(IServiceProvider services)
    {
        var forceOption = new Option<bool>("--force", "-f")
        {
            Description = "Overwrite an existing manifest if one is present at the target path.",
        };

        var interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Walk through prompts to populate the first entry instead of writing the placeholder template.",
        };

        var command = new Command("init", "Create a starter conduit.json manifest. Writes to --manifest, or to $XDG_CONFIG_HOME/Zakira.Conduit/conduit.json.");
        command.Options.Add(forceOption);
        command.Options.Add(interactiveOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<InitCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                force: parseResult.GetValue(forceOption),
                interactive: parseResult.GetValue(interactiveOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildPinOrUpdateCommand(IServiceProvider services, string verb, string description)
    {
        var entryOption = new Option<string[]>("--entry", "-e")
        {
            Description = "Limit the operation to the named entry. Repeatable, and comma-separated values are accepted (e.g. '--entry a,b -e c').",
            AllowMultipleArgumentsPerToken = true,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Report what would change, without rewriting the manifest.",
        };

        var command = new Command(verb, description);
        command.Options.Add(entryOption);
        command.Options.Add(dryRunOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<PinUpdateCommandHandler>();
            return handler.InvokeAsync(
                verb: verb,
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                entries: EntryFilter.Normalise(parseResult.GetValue(entryOption)),
                dryRun: parseResult.GetValue(dryRunOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildWatchCommand(IServiceProvider services)
    {
        var debounceOption = new Option<int>("--debounce")
        {
            Description = "Time in milliseconds to coalesce burst writes to the manifest. Default: 250.",
            DefaultValueFactory = _ => 250,
        };

        var parallelOption = new Option<int>("--parallel", "-p")
        {
            Description = "Maximum number of entries to sync in parallel for each re-run. Default: 4.",
            DefaultValueFactory = _ => 4,
        };

        var command = new Command("watch", "Run an initial sync, then re-sync whenever the manifest changes on disk. Ctrl+C to stop.");
        command.Options.Add(debounceOption);
        command.Options.Add(parallelOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<WatchCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                debounceMs: parseResult.GetValue(debounceOption),
                maxParallelism: parseResult.GetValue(parallelOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildStatusCommand(IServiceProvider services)
    {
        var command = new Command("status", "Show what each manifest entry was last synced to (no network IO).");

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<StatusCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildCleanCommand(IServiceProvider services)
    {
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Report which destination directories would be removed, without touching the filesystem or state.",
        };

        var yesOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip the interactive confirmation prompt. Required when stdin isn't a TTY or when -o json is set.",
        };

        var command = new Command("clean", "Remove destination directories whose owning entry has been deleted from the manifest. Prompts before deleting unless --yes is supplied.");
        command.Options.Add(dryRunOption);
        command.Options.Add(yesOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<CleanCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                dryRun: parseResult.GetValue(dryRunOption),
                yes: parseResult.GetValue(yesOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildUnpinCommand(IServiceProvider services)
    {
        var entryOption = new Option<string[]>("--entry", "-e")
        {
            Description = "Limit the operation to the named entry. Repeatable, and comma-separated values are accepted (e.g. '--entry a,b -e c').",
            AllowMultipleArgumentsPerToken = true,
        };

        var toOption = new Option<string?>("--to")
        {
            Description = "Branch name to thaw the pinned entry to (e.g. '--to main'). When omitted, Conduit queries the repo's default branch via the GitHub / AzDO API and falls back to 'main' if discovery fails.",
        };

        var toDefaultOption = new Option<bool>("--to-default")
        {
            Description = "Explicitly opt into default-branch API discovery (same as supplying no '--to' flag), but surface any discovery failure as an error rather than silently falling back to 'main'.",
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Report what would change, without rewriting the manifest.",
        };

        var command = new Command("unpin", "Restore branch tracking on pinned entries by rewriting tree/<sha>/<path> back to tree/<branch>/<path> (GitHub) or ?version=GC<sha> back to ?version=GB<branch> (AzDO). Object sources have their 'commit' replaced with 'branch'.");
        command.Options.Add(entryOption);
        command.Options.Add(toOption);
        command.Options.Add(toDefaultOption);
        command.Options.Add(dryRunOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<UnpinCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                entries: EntryFilter.Normalise(parseResult.GetValue(entryOption)),
                toBranch: parseResult.GetValue(toOption),
                toDefault: parseResult.GetValue(toDefaultOption),
                dryRun: parseResult.GetValue(dryRunOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildCopyCommand(IServiceProvider services)
    {
        var sourceArg = new Argument<string>("source")
        {
            Description = "Source URI or local path (e.g. 'https://github.com/owner/repo', 'https://github.com/owner/repo -> Alias', './local/dir'). Same syntax as a manifest 'source' string.",
        };

        var targetArg = new Argument<string>("target")
        {
            Description = "Target directory. The chosen strategy decides what gets written inside it.",
        };

        var strategyOption = new Option<string?>("--strategy", "-s")
        {
            Description = "Which strategy controls the destination layout. One of: wrap (default), flat, expand, skills.",
        };

        var groupByOption = new Option<string?>("--group-by")
        {
            Description = "When set to 'source', wrap each source's planned output in a source-named sub-directory under the target.",
        };

        var onCollisionOption = new Option<string?>("--on-collision")
        {
            Description = "Resolution mode for duplicate planned destinations. One of: error (default), skip, last-wins.",
        };

        var skillsOption = new Option<string[]>("--skills")
        {
            Description = "Skills-strategy filter: restrict to the named skills (by SKILL.md folder basename). Repeatable; comma-separated values accepted.",
            AllowMultipleArgumentsPerToken = true,
        };

        var harnessOption = new Option<string[]>("--harness")
        {
            Description = "Skills-strategy filter for harness discovery. Pass 'true' to require discovery (error if none match), 'false' to skip discovery entirely, or one or more harness names. Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };

        var addHarnessOption = new Option<string[]>("--add-harness")
        {
            Description = "Add a custom harness to the registry for this run. Format: 'name=path' (e.g. '--add-harness .my-tool=skills/'). Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Plan and report what would change without writing to the target.",
        };

        var command = new Command("copy", "One-shot copy of a single source into a single target using a chosen strategy. Bypasses the manifest entirely; equivalent to a synthetic one-entry manifest run.");
        command.Arguments.Add(sourceArg);
        command.Arguments.Add(targetArg);
        command.Options.Add(strategyOption);
        command.Options.Add(groupByOption);
        command.Options.Add(onCollisionOption);
        command.Options.Add(skillsOption);
        command.Options.Add(harnessOption);
        command.Options.Add(addHarnessOption);
        command.Options.Add(dryRunOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<CopyCommandHandler>();
            return handler.InvokeAsync(
                sourceUri: parseResult.GetValue(sourceArg) ?? string.Empty,
                targetPath: parseResult.GetValue(targetArg) ?? string.Empty,
                strategy: parseResult.GetValue(strategyOption),
                groupBy: parseResult.GetValue(groupByOption),
                onCollision: parseResult.GetValue(onCollisionOption),
                skills: EntryFilter.Normalise(parseResult.GetValue(skillsOption)),
                harness: EntryFilter.Normalise(parseResult.GetValue(harnessOption)),
                addHarness: parseResult.GetValue(addHarnessOption),
                dryRun: parseResult.GetValue(dryRunOption),
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildSkillsCommand(IServiceProvider services)
    {
        var skills = new Command("skills", "Utilities for the skills strategy: discover harness layouts under a target ('probe'). Per-entry sync continues to live on 'conduit sync --entry <name>'.");
        skills.Subcommands.Add(BuildSkillsProbeCommand(services));
        return skills;
    }

    private static Command BuildSkillsProbeCommand(IServiceProvider services)
    {
        var targetArg = new Argument<string>("target")
        {
            Description = "Target directory to scan for known agent harnesses (e.g. ~, ~/projects/my-app, $XDG_CONFIG_HOME/agents).",
        };

        var command = new Command("probe", "Scan a target directory one level deep for registered agent-harness skills folders (.opencode/skills, .claude/skills, .codex/skills, .agents/skills, plus any custom additions from the manifest). Exits non-zero when nothing matches.");
        command.Arguments.Add(targetArg);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<SkillsProbeCommandHandler>();
            return handler.InvokeAsync(
                targetPath: parseResult.GetValue(targetArg) ?? string.Empty,
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                output: parseResult.GetValue(CommonOptions.Output),
                cancellationToken: cancellationToken);
        });

        return command;
    }
}
