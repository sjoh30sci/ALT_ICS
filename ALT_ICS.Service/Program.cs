using ALT_ICS.Service.Logging;
using ALT_ICS.Service.Services;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service;

/// <summary>
/// Application entry point. Configures and runs the Windows Service host.
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // ---------- configuration ----------
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        // ---------- services ----------
        builder.Services.AddSingleton<NetworkConfig>();
        builder.Services.AddSingleton<ServiceEventLogger>();

        // Register the NAT service (concrete type for DI).
        builder.Services.AddSingleton<NATConnectionService>();
        builder.Services.AddSingleton<INATService>(sp => sp.GetRequiredService<NATConnectionService>());

        // Register the connection manager.
        builder.Services.AddSingleton<NetworkSharingService>();
        builder.Services.AddSingleton<IConnectionManager>(sp => sp.GetRequiredService<NetworkSharingService>());

        // Register sub-services.
        builder.Services.AddSingleton<DHCPServer>();
        builder.Services.AddSingleton<DNSRelayService>();

        // Register the background worker that drives the service lifecycle.
        builder.Services.AddHostedService<WindowsServiceHost>();

        // ---------- logging ----------
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = Constants.EventLogSource;
            settings.LogName = Constants.EventLogName;
        });

        // ---------- build & run ----------
        var host = builder.Build();

        // Apply configuration from appsettings into NetworkConfig singleton.
        var config = host.Services.GetRequiredService<NetworkConfig>();
        builder.Configuration.Bind("NetworkConfig", config);

        await host.RunAsync();
    }
}
