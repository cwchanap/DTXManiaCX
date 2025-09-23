using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DTXManiaCX.MCP.Server.Services;
using DTXManiaCX.MCP.Server.Tools;
using DTXManiaCX.MCP.Server.Console;
using ModelContextProtocol.Server;

namespace DTXManiaCX.MCP.Server;

class Program
{
    static async Task Main(string[] args)
    {
        // Check if we should run in test mode
        bool testMode = args.Length > 0 && args[0] == "--test";
        
        if (testMode)
        {
            await RunTestModeAsync();
        }
        else
        {
            await RunServerModeAsync(args);
        }
    }
    
    static async Task RunTestModeAsync()
    {
        System.Console.WriteLine("Running in test mode...");
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        var testConsole = serviceProvider.GetRequiredService<GameInteractionTestConsole>();
        await testConsole.RunTestsAsync();
        
        if (!Environment.UserInteractive || System.Console.IsInputRedirected)
        {
            System.Console.WriteLine("Test run complete.");
            return;
        }

        System.Console.WriteLine("Press any key to exit...");

        try
        {
            System.Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // Non-interactive environments throw when no console is attached; ignore.
        }
    }
    
    static async Task RunServerModeAsync(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        
        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            var logger = host.Services.GetService<ILogger<Program>>();
            logger?.LogCritical(ex, "Application terminated unexpectedly");
        }
    }
    
    static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<GameStateManager>();
        services.AddSingleton<GameInteractionService>();
        services.AddSingleton<GameInteractionTools>();
        services.AddSingleton<GameInteractionTestConsole>();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        var mcpBuilder = services.AddMcpServer(_ => { });

        mcpBuilder
            .WithStdioServerTransport()
            .WithTools<GameInteractionMcpToolHandlers>();
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
                services.AddHostedService<McpServerService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });
}
