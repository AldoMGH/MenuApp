using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Infrastructure;
using InfraDI = MenuPlanner.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Servidor MCP por stdio para Claude Desktop. stdout es el canal del protocolo: todos los logs van a stderr.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(
    builder.Configuration.GetSection("MenuGeneration").Get<GenerationOptions>() ?? new GenerationOptions());
builder.Services.AddMenuPlannerApplication();
builder.Services.AddMenuPlannerInfrastructure(builder.Configuration);

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "menu-planner", Version = "1.0.0" })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

if (!InfraDI.UsesMemoryStorage(builder.Configuration) && builder.Configuration.GetValue("Seed:Enabled", true))
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MenuPlanner.Mcp");
    await InfraDI.InitializeDatabaseAsync(host.Services, InfraDI.SeedPath(builder.Configuration), logger);
}

await host.RunAsync();
