using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LL.Hosting.Tests;

public sealed class TransportSecurityTests
{
    [Fact]
    public async Task Redirects_http_requests_to_https_outside_the_configured_environment()
    {
        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseApiTransportSecurity(app.Environment);
                app.MapGet("/", () => "ok");
            },
            environment: "Production",
            httpsPort: 443);

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location?.Scheme);
    }

    [Fact]
    public async Task Does_not_redirect_in_the_configured_environment()
    {
        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseApiTransportSecurity(app.Environment);
                app.MapGet("/", () => "ok");
            },
            environment: "Testing",
            httpsPort: 443);

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sends_hsts_outside_development()
    {
        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.Use(ForceHttps);
                app.UseApiTransportSecurity(app.Environment);
                app.MapGet("/", () => "ok");
            },
            environment: "Production");

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "api.example";
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Sends_no_hsts_in_development()
    {
        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.Use(ForceHttps);
                app.UseApiTransportSecurity(app.Environment);
                app.MapGet("/", () => "ok");
            },
            environment: "Development");

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "api.example";
        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static Task ForceHttps(HttpContext context, RequestDelegate next)
    {
        context.Request.Scheme = "https";

        return next(context);
    }
}
