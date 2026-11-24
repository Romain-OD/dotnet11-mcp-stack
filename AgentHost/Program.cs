using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OllamaSharp;

var builder = Host.CreateApplicationBuilder(args);

if (args.Contains("--ollama"))
    builder.Services.AddChatClient(new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3:4b"));
else
    builder.Services.AddChatClient(new AzureOpenAIClient(new Uri(builder.Configuration["AZURE_OPENAI_ENDPOINT"]!), new DefaultAzureCredential())
        .GetChatClient(builder.Configuration["AZURE_OPENAI_DEPLOYMENT"] ?? "gpt-4.1-mini").AsIChatClient());

using var host = builder.Build();

await using var mcp = await McpClient.CreateAsync(
    new HttpClientTransport(new() { Endpoint = new Uri("http://localhost:5000") }),
    new McpClientOptions { Handlers = new() { ElicitationHandler = AskUser } });

var tools = (await mcp.ListToolsAsync())
    .Where(t => t.Name is "get_stock_level" or "adjust_inventory")
    .ToList();
Console.WriteLine($"Tools: {string.Join(", ", tools.Select(t => t.Name))}");

if (args.Contains("probe"))
{
    // No model: call the tools directly to check MRTR and pod affinity.
    var sku = args.SkipWhile(a => a != "probe").Skip(1).FirstOrDefault() ?? "A-42";
    Console.WriteLine(Text(await mcp.CallToolAsync("get_stock_level", new Dictionary<string, object?> { ["sku"] = sku })));
    Console.WriteLine(Text(await mcp.CallToolAsync("adjust_inventory", new Dictionary<string, object?> { ["sku"] = sku, ["delta"] = -150 })));
    return;
}

var agent = host.Services.GetRequiredService<IChatClient>().AsAIAgent(
    instructions: "You manage warehouse stock. Check the stock level before you adjust it.",
    tools: [.. tools]);

await foreach (var update in agent.RunStreamingAsync("Ship 150 units of SKU A-42."))
    Console.Write(update);
Console.WriteLine();

static string Text(CallToolResult r) => string.Join(" ", r.Content.OfType<TextContentBlock>().Select(c => c.Text));

static ValueTask<ElicitResult> AskUser(ElicitRequestParams? request, CancellationToken ct)
{
    Console.Write($"{request?.Message} [y/N] ");
    var yes = Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    return ValueTask.FromResult(new ElicitResult
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement> { ["approve"] = JsonSerializer.SerializeToElement(yes) },
    });
}



