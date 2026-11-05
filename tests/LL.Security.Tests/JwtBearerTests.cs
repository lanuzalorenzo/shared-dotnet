using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LL.Security.Tests;

public sealed class JwtBearerTests
{
    private const string Authority = "https://login.microsoftonline.com/contoso/v2.0";
    private const string Audience = "api://orders";

    [Fact]
    public void Enables_every_validation_and_keeps_the_configured_values()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHardenedJwtBearer(options =>
        {
            options.Authority = Authority;
            options.Audience = Audience;
        });

        using var provider = services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(Authority, jwt.Authority);
        Assert.Equal(Audience, jwt.Audience);

        var parameters = jwt.TokenValidationParameters;
        Assert.True(parameters.ValidateIssuer);
        Assert.Equal(Authority, parameters.ValidIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal(Audience, parameters.ValidAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.Equal(TimeSpan.FromMinutes(2), parameters.ClockSkew);
    }

    [Fact]
    public void Registers_bearer_as_the_default_scheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHardenedJwtBearer(options =>
        {
            options.Authority = Authority;
            options.Audience = Audience;
        });

        using var provider = services.BuildServiceProvider();
        var authentication = provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authentication.DefaultScheme);
    }

    [Theory]
    [InlineData("", Audience)]
    [InlineData(Authority, "")]
    public void Fails_at_startup_when_the_issuer_or_the_audience_is_missing(string authority, string audience)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddHardenedJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
        }));
    }

    [Fact]
    public void Rejects_a_negative_clock_skew()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddHardenedJwtBearer(options =>
        {
            options.Authority = Authority;
            options.Audience = Audience;
            options.ClockSkew = TimeSpan.FromSeconds(-1);
        }));
    }
}
