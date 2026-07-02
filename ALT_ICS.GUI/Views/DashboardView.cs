using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using Spectre.Console;

namespace ALT_ICS.GUI.Views;

/// <summary>
/// Real-time monitoring dashboard rendered with Spectre.Console.
/// </summary>
public class DashboardView
{
    /// <summary>
    /// Runs the live dashboard loop until the user presses a key.
    /// </summary>
    /// <param name="refreshSeconds">Polling interval in seconds.</param>
    public async Task RunAsync(int refreshSeconds = 2)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow]ALT_ICS — Real-Time Dashboard[/]"));

        // TODO: Connect to the service via SignalR for live data.
        // For now, render a static placeholder.

        await AnsiConsole.Live(new Panel("Press [yellow]Q[/] to return to CLI").Header("Status"))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                using var cts = new CancellationTokenSource();
                var keyTask = Task.Run(() =>
                {
                    while (true)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                }, cts.Token);

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var health = await GetSampleHealthAsync();
                        RenderContent(ctx, health);
                        await Task.Delay(refreshSeconds * 1000, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Exit gracefully.
                }
            });

        AnsiConsole.MarkupLine("[green]Dashboard closed.[/]");
    }

    private static void RenderContent(LiveDisplayContext ctx, HealthReport health)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(
            RenderComponentPanel("NAT",  health.Components.ElementAtOrDefault(0)),
            RenderComponentPanel("DHCP", health.Components.ElementAtOrDefault(1)));
        grid.AddRow(
            RenderComponentPanel("DNS", health.Components.ElementAtOrDefault(2)),
            RenderStatsPanel());

        ctx.UpdateTarget(grid);
    }

    private static Panel RenderComponentPanel(string name, ComponentHealth? health)
    {
        var color = health?.IsHealthy == true ? "green" : "red";
        var status = health?.IsHealthy == true ? "● Healthy" : "○ Unhealthy";
        var message = health?.Message ?? "No data";

        var content = new Markup($"[{color}]{status}[/]\n{message}");
        return new Panel(content)
            .Header(name)
            .RoundedBorder();
    }

    private static Panel RenderStatsPanel()
    {
        // TODO: Show real stats from the service.
        var content = new Markup(
            "Active Sessions: [yellow]0[/]\n" +
            "Throughput:      [yellow]0 bps[/]\n" +
            "Total Forwarded: [yellow]0 bytes[/]");
        return new Panel(content)
            .Header("Statistics")
            .RoundedBorder();
    }

    /// <summary>
    /// Renders a one-shot health report (used by <c>status</c> command).
    /// </summary>
    public static void RenderHealth(HealthReport health)
    {
        var table = new Table();
        table.AddColumn("Component");
        table.AddColumn("Status");
        table.AddColumn("Message");

        foreach (var c in health.Components)
        {
            var status = c.IsHealthy ? "[green]● Healthy[/]" : "[red]○ Unhealthy[/]";
            table.AddRow(c.ComponentName, status, c.Message ?? "");
        }

        AnsiConsole.Write(table);
    }

    private static async Task<HealthReport> GetSampleHealthAsync()
    {
        // TODO: Replace with real call to service via SignalR.
        await Task.Delay(100);
        return new HealthReport
        {
            OverallHealth = false,
            Components = new List<ComponentHealth>
            {
                new() { ComponentName = "NAT",  IsHealthy = false, Message = "Not connected to service" },
                new() { ComponentName = "DHCP", IsHealthy = false, Message = "Not connected to service" },
                new() { ComponentName = "DNS",  IsHealthy = false, Message = "Not connected to service" }
            },
            ReportedAt = DateTime.UtcNow
        };
    }
}
