using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Arranca la aplicación con Kestrel de verdad en un puerto libre de loopback.
    /// TestServer no escribe la cabecera Server, así que la única forma de probar
    /// que deja de publicarse es hablar HTTP contra Kestrel.
    /// </summary>
    public static async Task<(WebApplication App, Uri Address)> StartKestrelAsync(
        Action<WebApplicationBuilder>? configureServices = null,
        Action<WebApplication>? configurePipeline = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        configureServices?.Invoke(builder);

        var app = builder.Build();
        configurePipeline?.Invoke(app);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = new Uri(addresses.First());

        return (app, address);
    }
}
