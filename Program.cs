using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services;

var builder = Host.CreateApplicationBuilder(args);

// MCP uses stdout for protocol messages. Keep application logs on stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<RagDocumentLoader>();
builder.Services.AddSingleton<RagIndexService>();

builder.Services.AddSingleton<ModelMemoryService>();
builder.Services.AddSingleton<TriggerAutomationService>();
builder.Services.AddSingleton<RuntimeGovernanceService>();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TriggerAutomationService>());
builder.Services.AddHostedService<MemoryBackgroundMaintenanceService>();
builder.Services.AddHostedService<AgentHostService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RuntimeGovernanceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<TimeTools>()
    .WithTools<CalcTools>()
    .WithTools<FileOperationsTools>()
    .WithTools<InternetSearchTools>()
    .WithTools<GitHubSearchTool>()
    .WithTools<RAGTool>()
    .WithTools<MemoryTools>()
    .WithTools<TriggerActionTools>()
    .WithTools<RuntimeTools>()
    .WithTools<TelegramTools>();

await builder.Build().RunAsync();
