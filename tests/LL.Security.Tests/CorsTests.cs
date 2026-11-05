using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security.Tests;

public sealed class CorsTests
{
    [Fact]
    public async Task Denies_every_origin_when_none_is_configured()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddDenyByDefaultCors(builder.Configuration),
            configurePipeline: app =>
            {
                app.UseCors("ApiCors");
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();
        using var response = await SendPreflight(client, "https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Allows_only_the_configured_origins()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder =>
            {
                // La política se registra antes de añadir el proveedor de configuración:
                // si leyera los orígenes al registrar, esta prueba no vería ninguno.
                builder.Services.AddDenyByDefaultCors(builder.Configuration);

                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = "https://app.example",
                });
            },
            configurePipeline: app =>
            {
                app.UseCors("ApiCors");
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();

        using var allowed = await SendPreflight(client, "https://app.example");
        Assert.Equal(
            "https://app.example",
            allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var denied = await SendPreflight(client, "https://evil.example");
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Rejects_an_empty_policy_name()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddDenyByDefaultCors(configuration, options => options.PolicyName = " "));
    }

    private static Task<HttpResponseMessage> SendPreflight(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        return client.SendAsync(request);
    }
}
