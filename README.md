# The .NET 11 MCP stack

Demo code for episode 1 of **MCP Architecture for .NET Developers** on the Dev Skills Unlock YouTube channel.

It covers a stateless MCP server running as two instances behind a YARP load balancer, an approval step (MRTR elicitation) that doesn't need sticky sessions, and a Microsoft Agent Framework agent that switches from Azure OpenAI to a local Ollama model by changing one line.

| Project | What it is |
|---|---|
| `InventoryMcpServer` | MCP C# SDK v2 server with two tools, `get_stock_level` and `adjust_inventory`. The inventory is a SQLite `stock.db` file shared by both instances. |
| `LoadBalancer` | YARP on `:5000`. It round-robins between the two instances and checks `/health` every 2 seconds. |
| `AgentHost` | MCP client and Agent Framework agent. It uses only the two inventory tools. |

## Requirements

- .NET 11 SDK
- For the agent: an Azure OpenAI deployment (`az login`), or [Ollama](https://ollama.com) with `ollama pull qwen3:4b`

## Run it

```powershell
# two server instances
cd InventoryMcpServer
dotnet run --no-launch-profile -- --urls http://localhost:5001
dotnet run --no-launch-profile -- --urls http://localhost:5002   # second terminal

# load balancer
cd LoadBalancer
dotnet run

# client
cd AgentHost
dotnet run -- --ollama probe   # no model needed: calls the tools directly and asks you to approve
$env:AZURE_OPENAI_ENDPOINT = "https://<your-resource>.openai.azure.com/"
dotnet run                 # agent on Azure OpenAI (AZURE_OPENAI_DEPLOYMENT defaults to gpt-4.1-mini)
dotnet run -- --ollama     # same agent on local qwen3:4b
```

To see failover, stop one server instance with Ctrl+C and run the probe again. Once the health check marks that instance unhealthy, the remaining one serves every call.

Delete `InventoryMcpServer/stock.db` to reset A-42 to 400 units.

## Notes

- The approval state (`requestState`) goes back to the server without a signature. Sign it before you trust it in production.
- The AG-UI endpoint shown in the video (`AddAGUIServer` + `MapAGUIServer`) isn't included here. It needs the preview package `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`.
