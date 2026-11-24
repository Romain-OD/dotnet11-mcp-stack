using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy().LoadFromMemory(
    [new RouteConfig { RouteId = "mcp", ClusterId = "pods", Match = new() { Path = "{**all}" } }],
    [new ClusterConfig
    {
        ClusterId = "pods",
        LoadBalancingPolicy = "RoundRobin",
        HealthCheck = new() { Active = new() { Enabled = true, Interval = TimeSpan.FromSeconds(2), Timeout = TimeSpan.FromSeconds(1), Path = "/health" } },
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["podA"] = new() { Address = "http://localhost:5001" },
            ["podB"] = new() { Address = "http://localhost:5002" },
        },
    }]);

var app = builder.Build();
app.MapReverseProxy();
app.Run("http://localhost:5000");

