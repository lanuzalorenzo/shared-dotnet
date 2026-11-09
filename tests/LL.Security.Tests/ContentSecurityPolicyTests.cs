using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security.Tests;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void Deny_all_serializes_the_four_directives_in_order()
    {
        var policy = ContentSecurityPolicy.DenyAll();

        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            policy.ToHeaderValue());
    }

    [Fact]
    public void Adds_a_directive_that_already_existed_once()
    {
        var policy = ContentSecurityPolicy.DenyAll()
            .Add("img-src", "'self'")
            .Add("img-src", "'self'", "https://cdn.example");

        Assert.Equal("'self' https://cdn.example", policy.Directives.Single(d => d.Key == "img-src").Value);
        Assert.Contains("img-src 'self' https://cdn.example", policy.ToHeaderValue(), StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_a_directive_without_values()
    {
        var policy = new ContentSecurityPolicy().Add("upgrade-insecure-requests");

        Assert.Equal("upgrade-insecure-requests", policy.ToHeaderValue());
    }

    [Fact]
    public void Remove_takes_the_directive_out()
    {
        var policy = ContentSecurityPolicy.DenyAll().Remove("form-action");

        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
            policy.ToHeaderValue());
    }

    [Theory]
    [InlineData("")]
    [InlineData("default src")]
    [InlineData("default-src;")]
    [InlineData("default_src")]
    public void Rejects_a_directive_that_is_not_a_name(string directive)
    {
        var policy = ContentSecurityPolicy.DenyAll();

        Assert.Throws<ArgumentException>(() => policy.Add(directive, "'self'"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("'self';")]
    [InlineData("'self'\nsomething")]
    public void Rejects_a_value_with_a_separator_or_a_line_break(string value)
    {
        var policy = ContentSecurityPolicy.DenyAll();

        Assert.Throws<ArgumentException>(() => policy.Add("script-src", value));
    }

    [Fact]
    public async Task Adds_the_policy_header()
    {
        var options = new SecurityHeadersOptions { Csp = ContentSecurityPolicy.DenyAll() };

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(options);
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Uses_the_report_only_header_when_asked()
    {
        var policy = ContentSecurityPolicy.DenyAll();
        policy.ReportOnly = true;
        var options = new SecurityHeadersOptions { Csp = policy };

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(options);
                app.MapGet("/", () => "ok");
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.True(response.Headers.Contains("Content-Security-Policy-Report-Only"));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Keeps_the_policy_that_the_response_already_has()
    {
        var options = new SecurityHeadersOptions { Csp = ContentSecurityPolicy.DenyAll() };

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(options);
                app.Run(context =>
                {
                    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
                    return context.Response.WriteAsync("ok");
                });
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(
            "default-src 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Keeps_the_policy_on_an_error_response()
    {
        var options = new SecurityHeadersOptions { Csp = ContentSecurityPolicy.DenyAll() };

        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddProblemDetails(),
            configurePipeline: app =>
            {
                app.UseSecurityHeaders(options);
                app.UseExceptionHandler();
                app.MapGet("/boom", () => { throw new InvalidOperationException("fallo de prueba"); });
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/boom");

        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Fails_at_registration_when_both_sources_define_the_header()
    {
        var options = new SecurityHeadersOptions { Csp = ContentSecurityPolicy.DenyAll() };
        options.Headers["Content-Security-Policy"] = "default-src 'self'";

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                Assert.Throws<InvalidOperationException>(() => app.UseSecurityHeaders(options));
                app.MapGet("/", () => "ok");
            });
    }

    [Fact]
    public async Task Fails_when_the_report_only_header_is_in_headers_too()
    {
        var options = new SecurityHeadersOptions { Csp = ContentSecurityPolicy.DenyAll() };
        options.Headers["Content-Security-Policy-Report-Only"] = "default-src 'self'";

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                Assert.Throws<InvalidOperationException>(() => app.UseSecurityHeaders(options));
                app.MapGet("/", () => "ok");
            });
    }

    [Fact]
    public async Task Fails_at_registration_when_the_policy_has_no_directives()
    {
        var options = new SecurityHeadersOptions { Csp = new ContentSecurityPolicy() };

        await using var app = await TestHost.StartAsync(
            configurePipeline: app =>
            {
                Assert.Throws<InvalidOperationException>(() => app.UseSecurityHeaders(options));
                app.MapGet("/", () => "ok");
            });
    }
}
