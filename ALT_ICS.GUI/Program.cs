using ALT_ICS.GUI.Commands;
using ALT_ICS.GUI.Services;
using ALT_ICS.GUI.Utils;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ALT_ICS.GUI;

/// <summary>
/// Spectre.Console CLI entry point for ALT_ICS management.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Show a banner unless --quiet is passed.
        if (!args.Contains("--quiet"))
            ConsoleUtils.PrintBanner();

        // Build a minimal DI container so commands can resolve services.
        var services = new ServiceCollection();
        services.AddSingleton<NetworkConfig>();
        services.AddSingleton<ServiceClient>();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var registrar = new TypeRegistrar(services);

        var app = new CommandApp<DefaultCommand>(registrar);
        app.Configure(config =>
        {
            config.SetApplicationName("altics");
            config.PropagateExceptions();

            config.AddCommand<StartSharingCommand>("start")
                .WithDescription("Start internet connection sharing");

            config.AddCommand<StopSharingCommand>("stop")
                .WithDescription("Stop internet connection sharing");

            config.AddCommand<StatusCommand>("status")
                .WithDescription("Show current sharing status");

            config.AddCommand<ConfigCommand>("config")
                .WithDescription("View or modify configuration");

            config.AddCommand<DashboardCommand>("dashboard")
                .WithDescription("Open the real-time monitoring dashboard");
        });

        return await app.RunAsync(args);
    }
}
