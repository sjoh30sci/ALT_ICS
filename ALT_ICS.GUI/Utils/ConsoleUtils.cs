using ALT_ICS.Shared.Utils;
using Spectre.Console;
using System.Reflection;

namespace ALT_ICS.GUI.Utils;

/// <summary>
/// Console rendering helpers for the ALT_ICS CLI.
/// </summary>
public static class ConsoleUtils
{
    /// <summary>
    /// Prints the ALT_ICS banner at startup.
    /// </summary>
    public static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("ALT_ICS")
            .Color(Color.Yellow)
            .Centered());

        AnsiConsole.WriteLine($"[dim]Alternative Internet Connection Sharing  v{GetInformationalVersion()}[/]");

        AnsiConsole.WriteLine("[dim]───[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Gets the informational version of the assembly.
    /// </summary>
    public static string GetInformationalVersion()
    {
        var assembly = typeof(ConsoleUtils).Assembly;
        var attr = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    }

    /// <summary>
    /// Prompts the user for confirmation (y/n).
    /// </summary>
    public static bool Confirm(string prompt)
    {
        return AnsiConsole.Confirm(prompt);
    }

    /// <summary>
    /// Shows a spinner while an async operation is in progress.
    /// </summary>
    public static async Task<T> ShowProgressAsync<T>(string message, Func<Task<T>> operation)
    {
        return await AnsiConsole.Status()
            .StartAsync(message, async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.SpinnerStyle(Style.Parse("yellow"));
                return await operation();
            });
    }

    /// <summary>
    /// Displays a table of key-value pairs (e.g. for showing configuration).
    /// </summary>
    public static void ShowKeyValueTable(string title, IEnumerable<(string Key, string Value)> items)
    {
        var table = new Table();
        table.AddColumn("Key");
        table.AddColumn("Value");

        foreach (var (key, value) in items)
            table.AddRow(key, value);

        AnsiConsole.Write(new Rule($"[yellow]{title}[/]"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {message}");
    }

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[green]{message}[/]");
    }
}
