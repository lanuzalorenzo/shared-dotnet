using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace LL.Hosting.Tests;

internal static class TestHost
{
    public static async Task<WebApplication> StartAsync(
        Action<WebApplicationBuilder>? configureServices = null,
        Action<WebApplication>? configurePipeline = null,
        string environment = "Testing",
        int? httpsPort = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
        });

        builder.WebHost.UseTestServer();

        if (httpsPort is not null)
        {
            builder.WebHost.UseSetting("HTTPS_PORT", httpsPort.Value.ToString());
        }

        configureServices?.Invoke(builder);

        var app = builder.Build();
        configurePipeline?.Invoke(app);

        await app.StartAsync();

        return app;
    }
}
