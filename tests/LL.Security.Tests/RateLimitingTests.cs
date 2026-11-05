using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task Rejects_requests_over_the_limit()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddRemoteIpRateLimiting(options =>
            {
                options.PermitLimit = 3;
                options.Window = TimeSpan.FromMinutes(1);
            }),
            configurePipeline: app =>
            {
                app.UseRateLimiter();
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Uses_the_custom_partition_key()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddRemoteIpRateLimiting(options =>
            {
                options.PermitLimit = 1;
                options.Window = TimeSpan.FromMinutes(1);
                options.PartitionKey = context => context.Request.Headers["X-Tenant"].ToString();
            }),
            configurePipeline: app =>
            {
                app.UseRateLimiter();
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();

        using var firstTenant = new HttpRequestMessage(HttpMethod.Get, "/");
        firstTenant.Headers.Add("X-Tenant", "tenant-a");
        using var first = await client.SendAsync(firstTenant);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var sameTenant = new HttpRequestMessage(HttpMethod.Get, "/");
        sameTenant.Headers.Add("X-Tenant", "tenant-a");
        using var second = await client.SendAsync(sameTenant);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        using var otherTenant = new HttpRequestMessage(HttpMethod.Get, "/");
        otherTenant.Headers.Add("X-Tenant", "tenant-b");
        using var third = await client.SendAsync(otherTenant);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Rejects_invalid_limits(int permitLimit, int windowMinutes)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddRemoteIpRateLimiting(options =>
        {
            options.PermitLimit = permitLimit;
            options.Window = TimeSpan.FromMinutes(windowMinutes);
        }));
    }
}
