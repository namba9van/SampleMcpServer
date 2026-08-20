//https://github.com/virex-84

using Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

//inject
builder.Services.AddSingleton<LmStudioEndpoint>();
builder.Services.AddSingleton<LmStudioModelDiscovery>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<RagDocumentLoader>();
builder.Services.AddSingleton<RagIndexService>();

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<CalcTools>()
    .WithTools<FileOperationsTools>()
    .WithTools<InternetSearchTools>()
    .WithTools<GitHubSearchTool>()
    .WithTools<RAGTool>();

await builder.Build().RunAsync();

