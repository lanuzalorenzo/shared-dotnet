using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LL.Security.Tests;

public sealed class LocalJwtIssuerTests
{
    private const string Audience = "api://orders";
    private const string Permission = "orders.read";

    [Fact]
    public void Configures_no_tenant_and_validates_the_local_issuer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var issuer = new LocalJwtIssuer(Audience);

        services.AddLocalJwtBearer(issuer);

        using var provider = services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Null(jwt.Authority);
        Assert.True(string.IsNullOrEmpty(jwt.MetadataAddress));
        Assert.Null(jwt.ConfigurationManager);

        var parameters = jwt.TokenValidationParameters;
        Assert.True(parameters.ValidateIssuer);
        Assert.Equal(issuer.Issuer, parameters.ValidIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal(issuer.Audience, parameters.ValidAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.Same(issuer.SigningKey, parameters.IssuerSigningKey);
        Assert.Equal(TimeSpan.FromMinutes(2), parameters.ClockSkew);
    }

    [Fact]
    public void Mints_a_token_with_the_expected_claims()
    {
        var issuer = new LocalJwtIssuer(Audience);

        var token = issuer.CreateTokenWithPermissions([Permission]);

        var parsed = new JsonWebToken(token);
        Assert.Equal(issuer.Issuer, parsed.Issuer);
        Assert.Equal(issuer.Audience, parsed.Audiences.Single());
        Assert.Equal("local-user", parsed.Subject);
        Assert.Contains(parsed.Claims, claim =>
            claim.Type == PermissionPolicyOptions.DefaultClaimType && claim.Value == Permission);
    }

    [Fact]
    public async Task Accepts_its_own_token()
    {
        var issuer = new LocalJwtIssuer(Audience);
        await using var app = await StartAsync(issuer);

        var response = await GetOrdersAsync(app, issuer.CreateTokenWithPermissions([Permission]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_another_key()
    {
        var issuer = new LocalJwtIssuer(Audience);
        var intruder = new LocalJwtIssuer(Audience);
        await using var app = await StartAsync(issuer);

        var response = await GetOrdersAsync(app, intruder.CreateTokenWithPermissions([Permission]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_token_with_another_issuer()
    {
        var issuer = new LocalJwtIssuer(Audience);
        await using var app = await StartAsync(issuer);

        var token = issuer.CreateTokenWithPermissions(
            [Permission],
            issuer: "https://other.test/");

        var response = await GetOrdersAsync(app, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_token_with_another_audience()
    {
        var issuer = new LocalJwtIssuer(Audience);
        await using var app = await StartAsync(issuer);

        var token = issuer.CreateTokenWithPermissions(
            [Permission],
            audience: "api://other");

        var response = await GetOrdersAsync(app, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        var issuer = new LocalJwtIssuer(Audience);
        await using var app = await StartAsync(issuer);

        var token = issuer.CreateTokenWithPermissions(
            [Permission],
            lifetime: TimeSpan.FromMinutes(-5));

        var response = await GetOrdersAsync(app, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(LocalJwtIssuer issuer)
    {
        return await TestHost.StartAsync(
            builder =>
            {
                builder.Services.AddPermissionPolicies();
                builder.Services.AddLocalJwtBearer(issuer);
            },
            pipeline =>
            {
                pipeline.UseAuthentication();
                pipeline.UseAuthorization();
                pipeline.MapGet("/orders", () => Results.Ok())
                    .RequirePermission(Permission);
            });
    }

    private static async Task<HttpResponseMessage> GetOrdersAsync(WebApplication app, string token)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }
}
