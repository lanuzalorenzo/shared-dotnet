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
    /// <summary>URL absoluta del emisor, por ejemplo la del tenant de Entra ID.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audiencia esperada en el token.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Margen de reloj tolerado al validar la vigencia.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(2);
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

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "Authority debe ser una URL absoluta del emisor.",
                nameof(configure));
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new ArgumentException("Audience no puede estar vacío.", nameof(configure));
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                options.ClockSkew,
                "ClockSkew no puede ser negativo.");
        }

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = options.ClockSkew,
                };
            });
    }
}
