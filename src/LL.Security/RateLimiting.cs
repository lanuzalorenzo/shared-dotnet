using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security;

/// <summary>
/// Límite de peticiones por origen. Es una protección básica contra abuso y no
/// sustituye a una puerta de enlace ni a un WAF.
/// </summary>
public sealed class RemoteIpRateLimitOptions
{
    public int PermitLimit { get; set; } = 120;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int QueueLimit { get; set; }

    /// <summary>
    /// Clave de partición. Por defecto, la IP remota; detrás de un proxy hay que
    /// configurar previamente las cabeceras reenviadas o la clave será la del proxy.
    /// </summary>
    public Func<HttpContext, string> PartitionKey { get; set; } =
        static context => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Registra un limitador global de ventana fija por origen. Responde 429 al
    /// superar el cupo y no encola peticiones.
    /// </summary>
    public static IServiceCollection AddRemoteIpRateLimiting(
        this IServiceCollection services,
        Action<RemoteIpRateLimitOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RemoteIpRateLimitOptions();
        configure?.Invoke(options);

        if (options.PermitLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                options.PermitLimit,
                "PermitLimit debe ser al menos 1.");
        }

        if (options.Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                options.Window,
                "Window debe ser mayor que cero.");
        }

        if (options.QueueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                options.QueueLimit,
                "QueueLimit no puede ser negativo.");
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    options.PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        QueueLimit = options.QueueLimit,
                    }));
        });

        return services;
    }
}
