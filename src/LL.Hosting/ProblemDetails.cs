using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Hosting;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Registra ProblemDetails, el formato de error que describe el problema con
    /// un tipo estable en lugar de devolver texto libre.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();

        return services;
    }

    /// <summary>
    /// Convierte las excepciones no controladas en una respuesta ProblemDetails.
    /// Va antes que el resto del pipeline, después de las cabeceras de seguridad.
    /// </summary>
    public static IApplicationBuilder UseApiProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();

        return app;
    }
}
