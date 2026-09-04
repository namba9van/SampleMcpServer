using Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Any(x => string.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = await McpSelfInspector.RunSelfTestAsync(args);
    return;
}

if (args.Any(x => string.Equals(x, "--inspect", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = await McpSelfInspector.RunInteractiveAsync(args);
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<LmStudioEndpoint>();
builder.Services.AddSingleton<LmStudioModelDiscovery>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<RagDocumentLoader>();
builder.Services.AddSingleton<RagIndexService>();

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<TimeTools>()
    .WithTools<CalcTools>()
    .WithTools<FileOperationsTools>()
    .WithTools<InternetSearchTools>()
    .WithTools<GitHubSearchTool>()
    .WithTools<RAGTool>();

var host = builder.Build();

await host.RunAsync();
