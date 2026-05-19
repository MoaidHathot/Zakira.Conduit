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

        CommonOptions.Manifest.Recursive = true;

        root.Subcommands.Add(BuildSyncCommand(services));
        root.Subcommands.Add(BuildListCommand(services));
        root.Subcommands.Add(BuildValidateCommand(services));
        root.Subcommands.Add(BuildInitCommand(services));

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

        var command = new Command("sync", "Synchronize manifest entries into their target directories.");
        command.Options.Add(entryOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(stopOnFirstErrorOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<SyncCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                entries: parseResult.GetValue(entryOption) ?? Array.Empty<string>(),
                dryRun: parseResult.GetValue(dryRunOption),
                stopOnFirstError: parseResult.GetValue(stopOnFirstErrorOption),
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

        var command = new Command("init", "Create a starter conduit.json manifest. Writes to --manifest, or to $XDG_CONFIG_HOME/conduit/conduit.json.");
        command.Options.Add(forceOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var handler = services.GetRequiredService<InitCommandHandler>();
            return handler.InvokeAsync(
                manifest: parseResult.GetValue(CommonOptions.Manifest)?.FullName,
                force: parseResult.GetValue(forceOption),
                cancellationToken: cancellationToken);
        });

        return command;
    }
}
