using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LL.Observabilidad;

/// <summary>Registro de la observabilidad en el contenedor del consumidor.</summary>
public static class ObservabilidadExtensions
{
    /// <summary>
    /// Registra la fuente de trazas y métricas, sus opciones y el registro de
    /// transiciones como singletons. El consumidor añade el registro de logs y
    /// configura su exportador con el nombre de la fuente.
    /// </summary>
    public static IServiceCollection AddObservabilidad(
        this IServiceCollection services,
        Action<OpcionesDeObservabilidad>? configurar = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var opciones = new OpcionesDeObservabilidad();
        configurar?.Invoke(opciones);
        opciones.Validar();

        services.TryAddSingleton(opciones);
        services.TryAddSingleton(_ => new Telemetria(opciones.NombreDeLaFuente));
        services.TryAddSingleton<RegistroDeEventos>();

        return services;
    }
}
