using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace LL.Security.Tests;

internal static class TestHost
{
    public static async Task<WebApplication> StartAsync(
        Action<WebApplicationBuilder>? configureServices = null,
        Action<WebApplication>? configurePipeline = null,
        string environment = "Testing")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
        });

        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder);

        var app = builder.Build();
        configurePipeline?.Invoke(app);

        await app.StartAsync();

        return app;
    }
}
