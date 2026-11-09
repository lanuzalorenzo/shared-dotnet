using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security.Tests;

public sealed class SecurityHeadersTests
{
    [Fact]
    public async Task Adds_the_default_headers_to_every_response()
    {
        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders();
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(
            "geolocation=(), microphone=(), camera=()",
            response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task Keeps_the_defaults_that_the_caller_replaces_and_adds_new_ones()
    {
        var options = new SecurityHeadersOptions();
        options.Headers["X-Frame-Options"] = "SAMEORIGIN";
        options.Headers["X-Custom-Header"] = "lab";

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(options);
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("lab", response.Headers.GetValues("X-Custom-Header").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Keeps_the_headers_on_an_error_response()
    {
        // El manejador de excepciones limpia la respuesta; las cabeceras se
        // registran al empezar a escribir para sobrevivir a ese borrado.
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddProblemDetails(),
            configurePipeline: app =>
            {
                app.UseSecurityHeaders();
                app.UseExceptionHandler();
                app.MapGet("/boom", () => { throw new InvalidOperationException("fallo de prueba"); });
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Does_not_publish_the_server_header_when_kestrel_serves()
    {
        var (app, address) = await TestHost.StartKestrelAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders();
                app.MapGet("/", () => "ok");
            });

        await using var running = app;
        using var client = new HttpClient { BaseAddress = address };
        using var response = await client.GetAsync("/");

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Publishes_the_server_header_when_the_consumer_opts_out()
    {
        var (app, address) = await TestHost.StartKestrelAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(new SecurityHeadersOptions { SuppressServerHeader = false });
                app.MapGet("/", () => "ok");
            });

        await using var running = app;
        using var client = new HttpClient { BaseAddress = address };
        using var response = await client.GetAsync("/");

        Assert.Equal("Kestrel", response.Headers.GetValues("Server").Single());
    }
}
