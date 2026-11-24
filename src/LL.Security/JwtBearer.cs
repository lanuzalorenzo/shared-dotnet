using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LL.Security;

/// <summary>
/// Parámetros del emisor de tokens. El paquete exige valores reales: si faltan,
/// el arranque falla en vez de aceptar tokens sin validar.
/// </summary>
public sealed class HardenedJwtBearerOptions
{
    internal static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>URL absoluta del emisor, por ejemplo la del tenant de Entra ID.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audiencia esperada en el token.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Margen de reloj tolerado al validar la vigencia.</summary>
    public TimeSpan ClockSkew { get; set; } = DefaultClockSkew;
}

public static class JwtBearerExtensions
{
    /// <summary>
    /// Registra JWT bearer con emisor, audiencia, vigencia y firma validados, y
    /// el esquema como predeterminado. No admite omisiones: cada comprobación que
    /// se puede desactivar se deja activada de forma explícita.
    /// </summary>
    public static AuthenticationBuilder AddHardenedJwtBearer(
        this IServiceCollection services,
        Action<HardenedJwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HardenedJwtBearerOptions();
        configure(options);

        return AddBearer(services, options.Authority, options.Audience, options.ClockSkew, signingKey: null);
    }

    /// <summary>
    /// Monta el mismo esquema con la misma validación, pero con la clave del
    /// emisor local en vez de la autoridad del tenant. El emisor no se descubre
    /// por red y la clave solo existe en el proceso que la creó: es una puerta
    /// explícita para pruebas y desarrollo, no un interruptor del registro real.
    /// </summary>
    public static AuthenticationBuilder AddLocalJwtBearer(
        this IServiceCollection services,
        LocalJwtIssuer issuer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(issuer);

        return AddBearer(services, issuer.Issuer, issuer.Audience, HardenedJwtBearerOptions.DefaultClockSkew, issuer.SigningKey);
    }

    private static AuthenticationBuilder AddBearer(
        IServiceCollection services,
        string issuer,
        string audience,
        TimeSpan clockSkew,
        SecurityKey? signingKey)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "El emisor debe ser una URL absoluta.",
                nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentException("Audience no puede estar vacío.", nameof(audience));
        }

        if (clockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clockSkew),
                clockSkew,
                "ClockSkew no puede ser negativo.");
        }

        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = clockSkew,
        };

        if (signingKey is not null)
        {
            validation.IssuerSigningKey = signingKey;
        }

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                if (signingKey is null)
                {
                    jwt.Authority = issuer;
                    jwt.Audience = audience;
                }
                else
                {
                    jwt.Authority = null;
                    jwt.MetadataAddress = string.Empty;
                    jwt.Audience = null;
                }

                jwt.TokenValidationParameters = validation;
            });
    }
}
