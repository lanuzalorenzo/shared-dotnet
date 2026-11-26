using LL.Eventos.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LL.Eventos.Worker;

/// <summary>Registro del worker en el contenedor del consumidor.</summary>
public static class EventosWorkerExtensions
{
    /// <summary>
    /// Añade el bucle de consumo, su sonda y el manejador. El consumidor
    /// registra antes el <see cref="EventosClient"/> con su transporte. El
    /// manejador se resuelve en un alcance por entrega, así que puede depender
    /// de servicios con ámbito, como un contexto de datos.
    /// </summary>
    public static IServiceCollection AddEventosWorker<THandler>(
        this IServiceCollection services,
        Action<EventosWorkerOptions>? configurar = null)
        where THandler : class, IEventosHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        var opciones = new EventosWorkerOptions();
        configurar?.Invoke(opciones);
        opciones.Validar();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(opciones);
        services.TryAddScoped<IEventosHandler, THandler>();
        services.TryAddSingleton(
            servicio => new SondaDeCola(
                opciones.UmbralDeAtasco,
                servicio.GetRequiredService<TimeProvider>()));
        services.AddHostedService(
            servicio => new EventosWorker(
                servicio.GetRequiredService<EventosClient>(),
                servicio.GetRequiredService<IServiceScopeFactory>(),
                opciones,
                servicio.GetRequiredService<SondaDeCola>(),
                servicio.GetService<ILogger<EventosWorker>>()));

        return services;
    }
}
