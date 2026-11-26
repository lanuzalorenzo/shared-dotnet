using System.Collections.Concurrent;
using LL.Eventos.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LL.Eventos.Worker;

/// <summary>
/// Bucle de consumo del contrato v1. Reclama una entrega por conversación,
/// aplica con el manejador y cierra con el hash del commit y el número de
/// entrega del evento. Al apagarse deja de reclamar, da un plazo a lo empezado
/// y devuelve con nack lo que no terminó, para no retener el lease.
/// </summary>
public sealed class EventosWorker : BackgroundService
{
    private const int LargoMaximoDelMotivo = 300;
    private static readonly TimeSpan MargenFinal = TimeSpan.FromSeconds(1);

    private readonly EventosClient _cliente;
    private readonly IServiceScopeFactory _alcances;
    private readonly EventosWorkerOptions _opciones;
    private readonly SondaDeCola _sonda;
    private readonly ILogger<EventosWorker> _registro;
    private readonly ConcurrentDictionary<Entrega, Guid> _pendientes = new();

    public EventosWorker(
        EventosClient cliente,
        IServiceScopeFactory alcances,
        EventosWorkerOptions opciones,
        SondaDeCola sonda,
        ILogger<EventosWorker>? registro = null)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(alcances);
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(sonda);

        opciones.Validar();

        _cliente = cliente;
        _alcances = alcances;
        _opciones = opciones;
        _sonda = sonda;
        _registro = registro ?? NullLogger<EventosWorker>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var plazo = new CancellationTokenSource();
        var bucles = new Task[_opciones.ConcurrenciaMaxima];

        for (var i = 0; i < bucles.Length; i++)
        {
            bucles[i] = BucleAsync(stoppingToken, plazo.Token);
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Parada pedida por el host.
        }

        var margen = Task.Delay(_opciones.TiempoDeApagado, CancellationToken.None);

        if (await Task.WhenAny(Task.WhenAll(bucles), margen).ConfigureAwait(false) != margen)
        {
            return;
        }

        // El plazo venció con entregas abiertas: se cancelan y se devuelve el lease.
        _registro.LogWarning(
            "Apagado sin margen: se devuelven {Pendientes} entregas sin cerrar.",
            _pendientes.Count);

        await plazo.CancelAsync().ConfigureAwait(false);
        await DevolverPendientesAsync().ConfigureAwait(false);
        await Task.WhenAny(Task.WhenAll(bucles), Task.Delay(MargenFinal)).ConfigureAwait(false);
    }

    private async Task BucleAsync(CancellationToken parada, CancellationToken plazo)
    {
        try
        {
            while (!parada.IsCancellationRequested)
            {
                Evento? evento;

                try
                {
                    // El reclamo ya enviado no se cancela: si el servidor concedió
                    // el lease y se pierde la respuesta, la entrega quedaría
                    // reclamada hasta que venciera. Si vuelve con la parada pedida,
                    // se devuelve en el acto.
                    evento = await _cliente.ClaimAsync(_opciones.Tipo, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception excepcion)
                {
                    _registro.LogWarning(
                        "Fallo al reclamar: {Error}; se reintenta tras la espera.",
                        excepcion.GetType().Name);
                    await EsperarAsync(_opciones.EsperaTrasError, parada).ConfigureAwait(false);
                    continue;
                }

                if (evento is null)
                {
                    if (parada.IsCancellationRequested)
                    {
                        return;
                    }

                    await EsperarAsync(_opciones.EsperaSinTrabajo, parada).ConfigureAwait(false);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(evento.Id))
                {
                    // Sin identificador no se puede ackear ni nackear: la entrega
                    // queda retenida hasta que venza el lease y el reclamo se repite.
                    _registro.LogWarning(
                        "El reclamo no trajo identificador; no se puede cerrar la entrega.");
                    await EsperarAsync(_opciones.EsperaTrasError, parada).ConfigureAwait(false);
                    continue;
                }

                if (parada.IsCancellationRequested)
                {
                    // La entrega llegó después de la parada: no se empieza trabajo.
                    await RechazarAsync(evento, "apagado").ConfigureAwait(false);
                    return;
                }

                await AplicarAsync(evento, plazo).ConfigureAwait(false);

                if (parada.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cierre pedido por el trabajador o por el plazo de apagado.
        }
        catch (Exception excepcion)
        {
            // Un fallo no previsto detiene ese bucle; queda registrado.
            _registro.LogError(
                "El bucle de consumo se detuvo por {Error}.",
                excepcion.GetType().Name);
        }
    }

    private async Task AplicarAsync(Evento evento, CancellationToken plazo)
    {
        var clave = new Entrega(evento.Id, evento.Intentos);
        var testigo = Guid.NewGuid();
        using var entrega = _sonda.EnCurso(evento.Id, evento.Intentos, evento.LeaseHasta);
        _pendientes[clave] = testigo;

        string? hash = null;
        Exception? fallo = null;

        try
        {
            await using var alcance = _alcances.CreateAsyncScope();
            var manejador = alcance.ServiceProvider.GetRequiredService<IEventosHandler>();

            hash = await manejador.HandleAsync(evento, plazo).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(hash))
            {
                hash = null;
                fallo = new InvalidOperationException("El manejador no devolvió el hash del commit.");
            }
        }
        catch (OperationCanceledException) when (plazo.IsCancellationRequested)
        {
            fallo = new OperationCanceledException();
        }
        catch (Exception excepcion)
        {
            fallo = excepcion;
        }

        // Un solo dueño del cierre: si el supervisor ya la devolvió, no se toca.
        if (!Quitar(clave, testigo))
        {
            return;
        }

        if (plazo.IsCancellationRequested)
        {
            await RechazarAsync(evento, "apagado").ConfigureAwait(false);
            return;
        }

        if (fallo is not null)
        {
            _registro.LogWarning(
                "El evento {Evento} (intento {Intento}) no se aplicó: {Error}.",
                evento.Id,
                evento.Intentos,
                fallo.GetType().Name);
            await RechazarAsync(evento, Motivo(fallo)).ConfigureAwait(false);
            return;
        }

        try
        {
            await _cliente
                .AckAsync(evento.Id, hash!, evento.Intentos, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (EventosClientException excepcion)
        {
            _registro.LogWarning(
                "No se pudo cerrar el evento {Evento} (intento {Intento}): {Estado}.",
                evento.Id,
                evento.Intentos,
                excepcion.Estado);
        }
        catch (Exception excepcion)
        {
            _registro.LogWarning(
                "No se pudo cerrar el evento {Evento} (intento {Intento}): {Error}.",
                evento.Id,
                evento.Intentos,
                excepcion.GetType().Name);
        }
    }

    private async Task RechazarAsync(Evento evento, string motivo)
    {
        try
        {
            await _cliente
                .NackAsync(evento.Id, motivo, evento.Intentos, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (EventosClientException excepcion)
        {
            // 409: el lease o el testigo cambiaron; la cola ya reasignó el trabajo.
            _registro.LogWarning(
                "No se pudo rechazar el evento {Evento} (intento {Intento}): {Estado}.",
                evento.Id,
                evento.Intentos,
                excepcion.Estado);
        }
        catch (Exception excepcion)
        {
            _registro.LogWarning(
                "No se pudo rechazar el evento {Evento} (intento {Intento}): {Error}.",
                evento.Id,
                evento.Intentos,
                excepcion.GetType().Name);
        }
    }

    private async Task DevolverPendientesAsync()
    {
        var devoluciones = new List<Task>(_pendientes.Count);

        foreach (var (clave, testigo) in _pendientes)
        {
            if (!Quitar(clave, testigo))
            {
                continue;
            }

            devoluciones.Add(DevolverAsync(clave));
        }

        await Task.WhenAll(devoluciones).ConfigureAwait(false);
    }

    /// <summary>Quita la entrega solo si sigue siendo la de este testigo.</summary>
    private bool Quitar(Entrega clave, Guid testigo)
    {
        return ((ICollection<KeyValuePair<Entrega, Guid>>)_pendientes)
            .Remove(new KeyValuePair<Entrega, Guid>(clave, testigo));
    }

    private async Task DevolverAsync(Entrega entrega)
    {
        try
        {
            await _cliente
                .NackAsync(entrega.Id, "apagado", entrega.Intento, CancellationToken.None)
                .ConfigureAwait(false);
            _registro.LogInformation(
                "Entrega devuelta al apagar: {Evento} (intento {Intento}).",
                entrega.Id,
                entrega.Intento);
        }
        catch (EventosClientException excepcion)
        {
            _registro.LogWarning(
                "No se pudo devolver la entrega {Evento} (intento {Intento}): {Estado}.",
                entrega.Id,
                entrega.Intento,
                excepcion.Estado);
        }
        catch (Exception excepcion)
        {
            _registro.LogWarning(
                "No se pudo devolver la entrega {Evento} (intento {Intento}): {Error}.",
                entrega.Id,
                entrega.Intento,
                excepcion.GetType().Name);
        }
    }

    private static async Task EsperarAsync(TimeSpan espera, CancellationToken cancelacion)
    {
        try
        {
            await Task.Delay(espera, cancelacion).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // La parada corta la espera.
        }
    }

    private static string Motivo(Exception excepcion)
    {
        var motivo = string.IsNullOrWhiteSpace(excepcion.Message)
            ? excepcion.GetType().Name
            : $"{excepcion.GetType().Name}: {excepcion.Message}";

        return motivo.Length <= LargoMaximoDelMotivo
            ? motivo
            : motivo[..LargoMaximoDelMotivo];
    }

    /// <summary>Una entrega concreta: el mismo evento puede reentregarse con otro intento.</summary>
    private readonly record struct Entrega(string Id, int Intento);
}
