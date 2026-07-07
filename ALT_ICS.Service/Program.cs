using ALT_ICS.Service.Hubs;
using ALT_ICS.Service.Logging;
using ALT_ICS.Service.Services;
using ALT_ICS.Shared.Models;
using ALT_ICS.Shared.Models.Interfaces;
using ALT_ICS.Shared.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ALT_ICS.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---------- configuration ----------
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        // ---------- services ----------
        builder.Services.AddSingleton<NetworkConfig>();
        builder.Services.AddSingleton<ServiceEventLogger>();

        builder.Services.AddSingleton<NATConnectionService>();
        builder.Services.AddSingleton<INATService>(sp => sp.GetRequiredService<NATConnectionService>());

        builder.Services.AddSingleton<NetworkSharingService>();
        builder.Services.AddSingleton<IConnectionManager>(sp => sp.GetRequiredService<NetworkSharingService>());

        builder.Services.AddSingleton<DHCPServer>();
        builder.Services.AddSingleton<DNSRelayService>();

        // SignalR for GUI communication
        builder.Services.AddSignalR();

        // Background worker that drives the service lifecycle
        builder.Services.AddHostedService<WindowsServiceHost>();

        // ---------- logging ----------
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = Constants.EventLogSource;
            settings.LogName = Constants.EventLogName;
        });

        // ---------- build ----------
        builder.Host.UseWindowsService();
        var app = builder.Build();

        // Apply configuration from appsettings into NetworkConfig singleton
        var config = app.Services.GetRequiredService<NetworkConfig>();
        app.Configuration.Bind("NetworkConfig", config);

        // Configure Kestrel to listen on the SignalR and health ports
        app.Urls.Add($"http://localhost:{Constants.SignalRPort}");
        app.Urls.Add($"http://localhost:{Constants.HealthHttpPort}");

        // Map SignalR hub for GUI communication
        app.MapHub<MonitorHub>(Constants.SignalRHubPath);

        // Map a simple health check endpoint
        app.MapGet("/health", () => Results.Ok(new { Status = "Running", Timestamp = DateTime.UtcNow }));

        // ---------- run ----------
        await app.RunAsync();
    }
}
