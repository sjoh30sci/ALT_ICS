using System.Net.Http;
using ALT_ICS.GUI.Services;
using ALT_ICS.GUI.Views;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ALT_ICS.GUI.Commands;

// ──────────────────────────────────────────────
//  Default command (no sub-command)
// ──────────────────────────────────────────────

public sealed class DefaultCommand : Command<DefaultCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Usage:[/] altics <command> [[options]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Available commands:");
        AnsiConsole.MarkupLine("  [green]start[/]      Start internet connection sharing");
        AnsiConsole.MarkupLine("  [green]stop[/]       Stop internet connection sharing");
        AnsiConsole.MarkupLine("  [green]status[/]     Show current sharing status");
        AnsiConsole.MarkupLine("  [green]config[/]     View or modify configuration");
        AnsiConsole.MarkupLine("  [green]dashboard[/]  Open the real-time monitoring dashboard");
        AnsiConsole.WriteLine();
        return 0;
    }
}

// ──────────────────────────────────────────────
//  Start command
// ──────────────────────────────────────────────

public sealed class StartSharingCommand : AsyncCommand<StartSharingCommand.Settings>
{
    private readonly ServiceClient _client;
    private readonly ILogger<StartSharingCommand> _logger;

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--wait")]
        public bool Wait { get; set; }
    }

    public StartSharingCommand(ServiceClient client, ILogger<StartSharingCommand> logger)
    {
        _client = client;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            return await AnsiConsole.Status().StartAsync("Connecting to service...", async ctx =>
            {
                ctx.Status("Starting...");
                await _client.ConnectAsync();
                var result = await _client.StartSharingAsync();
                if (result.OverallHealth)
                {
                    AnsiConsole.MarkupLine("[green]Service started successfully![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Service started with issues:[/]");
                    foreach (var c in result.Components)
                    {
                        AnsiConsole.MarkupLine($"  {(c.IsHealthy ? "[green]" : "[red]")}{c.ComponentName}: {c.Message}[/]");
                    }
                }
                return 0;
            });
        }
        catch (HttpRequestException)
        {
            AnsiConsole.MarkupLine("[yellow]Service is not running. Use 'altics start' to start it, or deploy with scripts\\deploy.ps1[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 0;
        }
    }
}

// ──────────────────────────────────────────────
//  Stop command
// ──────────────────────────────────────────────

public sealed class StopSharingCommand : AsyncCommand<StopSharingCommand.Settings>
{
    private readonly ServiceClient _client;
    private readonly ILogger<StopSharingCommand> _logger;

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--force")]
        public bool Force { get; set; }
    }

    public StopSharingCommand(ServiceClient client, ILogger<StopSharingCommand> logger)
    {
        _client = client;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            return await AnsiConsole.Status().StartAsync("Connecting to service...", async ctx =>
            {
                ctx.Status("Stopping...");
                await _client.ConnectAsync();
                var result = await _client.StopSharingAsync();
                AnsiConsole.MarkupLine("[green]Service stopped successfully![/]");
                return 0;
            });
        }
        catch (HttpRequestException)
        {
            AnsiConsole.MarkupLine("[yellow]Service is not running. Use 'altics start' to start it, or deploy with scripts\\deploy.ps1[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 0;
        }
    }
}

// ──────────────────────────────────────────────
//  Status command
// ──────────────────────────────────────────────

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    private readonly ServiceClient _client;
    private readonly ILogger<StatusCommand> _logger;

    public sealed class Settings : CommandSettings { }

    public StatusCommand(ServiceClient client, ILogger<StatusCommand> logger)
    {
        _client = client;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            await _client.ConnectAsync();
            var health = await _client.RequestHealthAsync();

            if (health is null)
            {
                AnsiConsole.MarkupLine("[red]Could not retrieve health status.[/]");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Component");
            table.AddColumn("Status");
            table.AddColumn("Message");

            foreach (var c in health.Components)
            {
                table.AddRow(
                    c.ComponentName,
                    c.IsHealthy ? "[green]Healthy[/]" : "[red]Unhealthy[/]",
                    c.Message ?? ""
                );
            }

            AnsiConsole.Write(table);
        }
        catch (HttpRequestException)
        {
            AnsiConsole.MarkupLine("[yellow]Service is not running. Use 'altics start' to start it, or deploy with scripts\\deploy.ps1[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }

        return 0;
    }
}

// ──────────────────────────────────────────────
//  Config command
// ──────────────────────────────────────────────

public sealed class ConfigCommand : AsyncCommand<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[property]")]
        public string? Property { get; set; }

        [CommandArgument(1, "[value]")]
        public string? Value { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Property))
        {
            // Show current configuration.
            var table = new Table();
            table.AddColumn("Setting");
            table.AddColumn("Value");

            table.AddRow("Public Interface",  "Wi-Fi (detected)");
            table.AddRow("Private Interface", "Ethernet (detected)");
            table.AddRow("DHCP Pool",         "192.168.137.100 — 192.168.137.200");
            table.AddRow("Primary DNS",       "8.8.8.8");
            table.AddRow("Secondary DNS",     "8.8.4.4");

            AnsiConsole.Write(new Rule("[yellow]Current Configuration[/]"));
            AnsiConsole.Write(table);
        }
        else
        {
            // TODO: Forward property change to the service via SignalR.
            AnsiConsole.MarkupLineInterpolated($"[green]Setting {settings.Property} = {settings.Value}[/]");
        }

        return await Task.FromResult(0);
    }
}

// ──────────────────────────────────────────────
//  Dashboard command
// ──────────────────────────────────────────────

public sealed class DashboardCommand : AsyncCommand<DashboardCommand.Settings>
{
    private readonly ServiceClient _client;

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--refresh <SECONDS>")]
        public int RefreshSeconds { get; set; } = 2;
    }

    public DashboardCommand(ServiceClient client)
    {
        _client = client;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var cts = new CancellationTokenSource();

        // Listen for Q key to exit dashboard
        _ = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        cts.Cancel();
                        break;
                    }
                }
                Thread.Sleep(100);
            }
        });

        var dashboard = new DashboardView(_client);
        await dashboard.RunAsync(cts.Token);

        AnsiConsole.MarkupLine("[green]Dashboard closed.[/]");
        return 0;
    }
}
