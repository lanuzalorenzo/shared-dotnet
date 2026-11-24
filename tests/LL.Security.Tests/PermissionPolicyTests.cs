using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security.Tests;

public sealed class PermissionPolicyTests
{
    private const string Audience = "api://orders";
    private const string Permission = "orders.read";

    [Fact]
    public async Task Allows_a_token_with_the_permission()
    {
        var (app, issuer) = await StartAsync();

        await using (app)
        {
            var token = issuer.CreateTokenWithPermissions([Permission]);

            var response = await GetOrdersAsync(app, token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Forbids_a_valid_token_without_the_permission()
    {
        var (app, issuer) = await StartAsync();

        await using (app)
        {
            var token = issuer.CreateTokenWithPermissions(["orders.write"]);

            var response = await GetOrdersAsync(app, token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Challenges_a_request_without_a_token()
    {
        var (app, _) = await StartAsync();

        await using (app)
        {
            var response = await GetOrdersAsync(app, token: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Matches_the_permission_exactly()
    {
        var (app, issuer) = await StartAsync();

        await using (app)
        {
            var token = issuer.CreateTokenWithPermissions(["orders.read.all"]);

            var response = await GetOrdersAsync(app, token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Reads_the_permission_from_the_configured_claim()
    {
        const string claimType = "app_permissions";
        var (app, issuer) = await StartAsync(options => options.ClaimType = claimType);

        await using (app)
        {
            var token = issuer.CreateTokenWithPermissions([Permission], claimType: claimType);

            var response = await GetOrdersAsync(app, token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Keeps_the_policies_registered_by_the_consumer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermissionPolicies();
        services.AddAuthorization(options =>
            options.AddPolicy("named", policy => policy.RequireAuthenticatedUser()));

        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotNull(await policies.GetPolicyAsync("named"));
        Assert.NotNull(await policies.GetPolicyAsync(PermissionPolicy.NameFor(Permission)));
        Assert.Null(await policies.GetPolicyAsync("missing"));
    }

    [Fact]
    public async Task Rejects_a_consumer_policy_that_uses_the_reserved_prefix()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermissionPolicies();
        services.AddAuthorization(options =>
            options.AddPolicy(
                PermissionPolicy.NameFor(Permission),
                policy => policy.RequireAssertion(_ => true)));

        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => policies.GetPolicyAsync(PermissionPolicy.NameFor(Permission)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" orders.read")]
    public void Rejects_an_invalid_permission_name(string permission)
    {
        Assert.Throws<ArgumentException>(() => PermissionPolicy.NameFor(permission));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" permissions")]
    public void Rejects_an_empty_or_padded_claim_type(string claimType)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddPermissionPolicies(options => options.ClaimType = claimType));
    }

    private static async Task<(WebApplication App, LocalJwtIssuer Issuer)> StartAsync(
        Action<PermissionPolicyOptions>? configurePolicies = null)
    {
        var issuer = new LocalJwtIssuer(Audience);

        var app = await TestHost.StartAsync(
            builder =>
            {
                builder.Services.AddPermissionPolicies(configurePolicies);
                builder.Services.AddLocalJwtBearer(issuer);
            },
            pipeline =>
            {
                pipeline.UseAuthentication();
                pipeline.UseAuthorization();
                pipeline.MapGet("/orders", () => Results.Ok())
                    .RequirePermission(Permission);
            });

        return (app, issuer);
    }

    private static async Task<HttpResponseMessage> GetOrdersAsync(WebApplication app, string? token)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }
}
