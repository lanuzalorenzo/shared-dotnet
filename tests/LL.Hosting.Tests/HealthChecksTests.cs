using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LL.Hosting.Tests;

public sealed class HealthChecksTests
{
    [Fact]
    public async Task Publishes_the_health_endpoint_without_authentication()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder =>
            {
                builder.Services.AddApiHealthChecks();
                builder.Services
                    .AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, RejectingAuthHandler>("Test", null);
                builder.Services.AddAuthorization(options =>
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build());
            },
            configurePipeline: app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapApiHealthChecks();
                app.MapGet("/private", () => "solo con token");
            });

        using var client = app.GetTestClient();

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());

        using var privateEndpoint = await client.GetAsync("/private");
        Assert.Equal(HttpStatusCode.Unauthorized, privateEndpoint.StatusCode);
    }

    [Fact]
    public async Task Accepts_a_custom_pattern()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddApiHealthChecks(),
            configurePipeline: app => app.MapApiHealthChecks("/healthz"));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

internal sealed class RejectingAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public RejectingAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
