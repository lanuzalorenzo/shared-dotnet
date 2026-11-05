using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Hosting;

public static class HealthCheckExtensions
{
    /// <summary>Registra la infraestructura de health checks.</summary>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    /// Publica el endpoint de salud sin autenticación. Es una decisión deliberada:
    /// la sonda de la plataforma no lleva token, y el endpoint devuelve solo el
    /// estado agregado, sin detalle de dependencias.
    /// </summary>
    public static IEndpointConventionBuilder MapApiHealthChecks(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/health")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("pattern no puede estar vacío.", nameof(pattern));
        }

        return endpoints.MapHealthChecks(pattern).AllowAnonymous();
    }
}
