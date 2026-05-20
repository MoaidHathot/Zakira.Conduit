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
        root.Subcommands.Add(BuildPinOrUpdateCommand(services, "pin", "Resolve every github entry's tracked branch to its current SHA and write the result into the manifest's 'commit' field."));
        root.Subcommands.Add(BuildPinOrUpdateCommand(services, "update", "Alias of 'pin'. Refresh each pinned entry to the latest SHA on its tracked branch."));
        root.Subcommands.Add(BuildWatchCommand(services));
        root.Subcommands.Add(BuildStatusCommand(services));

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
            Description = "Sync only the named entry (repeatable). When omitted, all enabled entries are synced.",
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

        var command = new Command("sync", "Synchronize manifest entries into their target directories.");
        command.Options.Add(entryOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(stopOnFirstErrorOption);
        command.Options.Add(forceOption);
        command.Options.Add(parallelOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<SyncCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                entries: parseResult.GetValue(entryOption) ?? Array.Empty<string>(),
                dryRun: parseResult.GetValue(dryRunOption),
                stopOnFirstError: parseResult.GetValue(stopOnFirstErrorOption),
                force: parseResult.GetValue(forceOption),
                maxParallelism: parseResult.GetValue(parallelOption),
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
                cancellationToken: cancellationToken);
        });

        return command;
    }

    private static Command BuildPinOrUpdateCommand(IServiceProvider services, string verb, string description)
    {
        var entryOption = new Option<string[]>("--entry", "-e")
        {
            Description = "Limit the operation to the named entry (repeatable).",
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
                entries: parseResult.GetValue(entryOption) ?? Array.Empty<string>(),
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
}
