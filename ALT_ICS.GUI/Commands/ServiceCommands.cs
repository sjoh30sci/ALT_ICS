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
        AnsiConsole.MarkupLine("[yellow]Requesting service start...[/]");

        // TODO: Implement actual SignalR call to the service.
        // await _client.StartSharingAsync();

        AnsiConsole.MarkupLine("[green]Start command sent to ALT_ICS service.[/]");
        return await Task.FromResult(0);
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
        AnsiConsole.MarkupLine("[yellow]Requesting service stop...[/]");

        // TODO: Implement actual SignalR call to the service.
        // await _client.StopSharingAsync();

        AnsiConsole.MarkupLine("[green]Stop command sent to ALT_ICS service.[/]");
        return await Task.FromResult(0);
    }
}

// ──────────────────────────────────────────────
//  Status command
// ──────────────────────────────────────────────

public sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine("[yellow]Querying ALT_ICS service status...[/]");

        // TODO: Call service health endpoint and display results.
        var health = new HealthReport
        {
            OverallHealth = false,
            Components = new List<ComponentHealth>
            {
                new() { ComponentName = "NAT",  IsHealthy = false, Message = "Not connected" },
                new() { ComponentName = "DHCP", IsHealthy = false, Message = "Not connected" },
                new() { ComponentName = "DNS",  IsHealthy = false, Message = "Not connected" }
            },
            ReportedAt = DateTime.UtcNow
        };

        DashboardView.RenderHealth(health);
        return await Task.FromResult(0);
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
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--refresh <SECONDS>")]
        public int RefreshSeconds { get; set; } = 2;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var dashboard = new DashboardView();
        await dashboard.RunAsync(settings.RefreshSeconds);
        return 0;
    }
}
