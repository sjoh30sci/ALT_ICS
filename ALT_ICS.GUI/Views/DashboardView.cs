using ALT_ICS.GUI.Services;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using Spectre.Console;

namespace ALT_ICS.GUI.Views;

/// <summary>
/// Real-time monitoring dashboard rendered with Spectre.Console.
/// </summary>
public class DashboardView
{
    private readonly ServiceClient _client;
    private HealthReport? _latestHealth;
    private NATStats? _latestStats;

    public DashboardView(ServiceClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Runs the live dashboard loop until cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Write(new Rule("[yellow]ALT_ICS Live Dashboard[/]"));
        AnsiConsole.WriteLine();

        if (!Console.IsOutputRedirected)
        {
            await _client.ConnectAsync();

            // Subscribe to live updates
            _client.OnHealthReportUpdated += report =>
            {
                _latestHealth = report;
            };
            _client.OnNatStatsUpdated += stats =>
            {
                _latestStats = stats;
            };

            // Live display loop
            await AnsiConsole.Live(new Panel("Initializing..."))
                .AutoClear(true)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var health = await _client.RequestHealthAsync(ct);
                            var stats = await _client.RequestNatStatsAsync(ct);

                            var grid = new Grid();
                            grid.AddColumn();
                            grid.AddColumn();

                            // Health panel
                            var healthPanel = new Panel(BuildHealthTable(health!))
                            {
                                Header = new PanelHeader("Service Health"),
                                Border = BoxBorder.Rounded
                            };

                            // Stats panel
                            var statsPanel = new Panel(BuildStatsTable(stats!))
                            {
                                Header = new PanelHeader("NAT Statistics"),
                                Border = BoxBorder.Rounded
                            };

                            grid.AddRow(healthPanel, statsPanel);
                            ctx.UpdateTarget(new Panel(grid) { Border = BoxBorder.None });
                            ctx.Refresh();
                        }
                        catch
                        {
                            // Not connected yet
                        }

                        await Task.Delay(2000, ct);
                    }
                });
        }
        else
        {
            // Output is redirected - show a static snapshot instead
            AnsiConsole.MarkupLine("[yellow]Dashboard requires an interactive console.[/]");
            AnsiConsole.MarkupLine("[grey]Run the GUI from a regular command prompt, not piped/redirected.[/]");

            try
            {
                await _client.ConnectAsync();
                var health = await _client.RequestHealthAsync(ct);
                AnsiConsole.Write(BuildHealthTable(health!));
            }
            catch
            {
                AnsiConsole.MarkupLine("[red]Could not connect to service.[/]");
            }
        }
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

    private static Table BuildHealthTable(HealthReport health)
    {
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

        return table;
    }

    private static Table BuildStatsTable(NATStats? stats)
    {
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");

        if (stats is null)
        {
            table.AddRow("Status", "[yellow]No data[/]");
            return table;
        }

        table.AddRow("Active Sessions", stats.ActiveSessions.ToString());
        table.AddRow("Throughput",      $"{stats.ThroughputBps} bps");
        table.AddRow("Total Forwarded", FormatBytes(stats.TotalBytesForwarded));
        table.AddRow("Packets Translated", stats.PacketsTranslated.ToString("N0"));
        table.AddRow("Packets Dropped", stats.PacketsDropped.ToString("N0"));

        return table;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }
}
