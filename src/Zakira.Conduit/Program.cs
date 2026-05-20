using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zakira.Conduit.Cli;
using Zakira.Conduit.Cli.Commands;
using Zakira.Conduit.DependencyInjection;

namespace Zakira.Conduit;

/// <summary>
///     <c>conduit</c> CLI entry point. Defines the command tree and dispatches
///     to handlers that resolve services from the DI container.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Verbosity has to be parsed up-front because logging is configured in
        // the DI container, which is built before the parse action runs.
        var verbosity = VerbosityParser.ParseFromArgs(args);

        await using var services = BuildServices(verbosity);

        var rootCommand = CommandTreeBuilder.Build(services);
        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    private static ServiceProvider BuildServices(LogLevel verbosity)
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging(builder =>
        {
            builder.SetMinimumLevel(verbosity);

            // Route ALL log lines to stderr regardless of level, so stdout
            // is reserved for the command's primary output (reports, JSON,
            // listings). This keeps stdout pipe-clean for scripting.
            builder.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.IncludeScopes = false;
                opts.TimestampFormat = null;
                opts.UseUtcTimestamp = false;
            });
        });

        serviceCollection.AddConduitCore();

        serviceCollection.AddSingleton(_ => ConsoleStyle.DetectFromEnvironment());

        // Command handlers.
        serviceCollection.AddSingleton<SyncCommandHandler>();
        serviceCollection.AddSingleton<ListCommandHandler>();
        serviceCollection.AddSingleton<ValidateCommandHandler>();
        serviceCollection.AddSingleton<InitCommandHandler>();
        serviceCollection.AddSingleton<PinUpdateCommandHandler>();
        serviceCollection.AddSingleton<WatchCommandHandler>();
        serviceCollection.AddSingleton<StatusCommandHandler>();

        return serviceCollection.BuildServiceProvider();
    }
}
