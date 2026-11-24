using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class InventoryTools(IConfiguration config, ILogger<InventoryTools> log)
{
    // one SQLite file shared by every instance on this machine; a real database when the pods are real.
    private readonly string _db = $"Data Source={config["StockDb"] ?? "stock.db"}";
    private string Pod => config["urls"] ?? "?";

    [McpServerTool, Description("Returns the quantity on hand for a SKU. Call this before AdjustInventory.")]
    public int GetStockLevel([Description("The SKU, for example A-42.")] string sku)
    {
        log.LogInformation("[{Pod}] GetStockLevel {Sku}", Pod, sku);
        return Read(sku);
    }

    [McpServerTool, Description("Adds units to (positive delta) or removes units from (negative delta) a SKU's stock and returns the new quantity. Adjustments over 100 units either way ask the user to confirm. Call GetStockLevel first.")]
    public string AdjustInventory(
        RequestContext<CallToolRequestParams> context,
        [Description("The SKU, for example A-42.")] string sku,
        [Description("Number of units: positive when stock arrives, negative when it ships.")] int delta)
    {
        if (Math.Abs(delta) > 100)
        {
            if (context.Params?.InputResponses?.TryGetValue("confirm", out var response) != true)
            {
                log.LogInformation("[{Pod}] AdjustInventory {Sku} {Delta}: asking for confirmation", Pod, sku, delta);
                throw new InputRequiredException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = $"Adjust {sku} by {delta} units?",
                            RequestedSchema = new()
                            {
                                Properties = { ["approve"] = new ElicitRequestParams.BooleanSchema { Description = "Approve the adjustment" } },
                                Required = ["approve"],
                            },
                        }),
                    },
                    requestState: JsonSerializer.Serialize(new { sku, delta }));
            }

            // request state is echoed by the client unsigned; sign it before trusting it in production.
            var answer = response!.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            var approved = answer is { Action: "accept" } && answer.Content?.TryGetValue("approve", out var v) == true && v.ValueKind == JsonValueKind.True;
            if (!approved)
            {
                log.LogInformation("[{Pod}] AdjustInventory {Sku} {Delta}: declined", Pod, sku, delta);
                return $"The user declined the adjustment of {sku} by {delta}. Stock is unchanged at {Read(sku)}.";
            }
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE stock SET qty = qty + $d WHERE sku = $s AND qty + $d >= 0 RETURNING qty";
        cmd.Parameters.AddWithValue("$d", delta);
        cmd.Parameters.AddWithValue("$s", sku);
        var qty = cmd.ExecuteScalar();
        log.LogInformation("[{Pod}] AdjustInventory {Sku} {Delta}: {Outcome}", Pod, sku, delta, qty is null ? "refused" : "applied");
        return qty is null ? $"Unknown SKU {sku}, or not enough stock to remove {-delta} units." : $"{sku} adjusted by {delta}. New quantity: {qty}.";
    }

    private int Read(string sku)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT qty FROM stock WHERE sku = $s";
        cmd.Parameters.AddWithValue("$s", sku);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_db);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS stock (sku TEXT PRIMARY KEY, qty INTEGER NOT NULL);
            INSERT OR IGNORE INTO stock VALUES ('A-42', 400), ('B-7', 35);
            """;
        cmd.ExecuteNonQuery();
        return conn;
    }
}
