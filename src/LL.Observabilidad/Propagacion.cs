using System.Diagnostics;

namespace LL.Observabilidad;

/// <summary>
/// Sobre mínimo para que la correlación viaje con el evento: el
/// <c>traceparent</c> y el <c>tracestate</c> de W3C, y el identificador que
/// etiqueta la traza. La cola lo guarda junto al evento y lo devuelve en el
/// reclamo; el consumidor abre su actividad con <see cref="Continuar"/>.
/// </summary>
public sealed record SobreDeTraza(string Traceparent, string? Tracestate, string EventoId);

/// <summary>
/// Propagación del contexto de traza entre la publicación y el cierre. Solo
/// usa el formato W3C: un identificador jerárquico o un <c>traceparent</c>
/// inválido se rechazan en vez de perder la correlación en silencio.
/// </summary>
public static class Propagacion
{
    private const string ClaveTraceparent = "traceparent";
    private const string ClaveTracestate = "tracestate";

    /// <summary>
    /// Captura el contexto de la actividad que publica. La actividad tiene que
    /// estar muestreada: sin escucha, el <see cref="ActivitySource"/> no crea
    /// actividad y no hay nada que propagar.
    /// </summary>
    public static SobreDeTraza Capturar(Activity actividad, string eventoId)
    {
        ArgumentNullException.ThrowIfNull(actividad);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

        if (actividad.IdFormat != ActivityIdFormat.W3C)
        {
            throw new InvalidOperationException(
                "La actividad tiene que usar el formato W3C para propagar el contexto.");
        }

        var portador = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DistributedContextPropagator.Current.Inject(actividad, portador, Escribir);

        if (!portador.TryGetValue(ClaveTraceparent, out var traceparent)
            || !ActivityContext.TryParse(
                traceparent,
                portador.GetValueOrDefault(ClaveTracestate),
                out _))
        {
            throw new InvalidOperationException(
                "La actividad no produjo un traceparent W3C válido.");
        }

        return new SobreDeTraza(
            traceparent,
            portador.GetValueOrDefault(ClaveTracestate),
            eventoId);
    }

    /// <summary>
    /// Abre la actividad del consumidor como hija del contexto recibido y la
    /// etiqueta con el identificador del sobre. El llamador la libera con
    /// <c>using</c>.
    /// </summary>
    public static Activity? Continuar(
        this Telemetria telemetria,
        string operacion,
        SobreDeTraza sobre,
        ActivityKind tipo = Telemetria.TipoDeCierre)
    {
        ArgumentNullException.ThrowIfNull(telemetria);
        ArgumentNullException.ThrowIfNull(sobre);

        if (string.IsNullOrWhiteSpace(sobre.Traceparent)
            || !ActivityContext.TryParse(sobre.Traceparent, sobre.Tracestate, out var contexto))
        {
            throw new ArgumentException(
                "El traceparent del sobre no es un contexto W3C válido.",
                nameof(sobre));
        }

        if (string.IsNullOrWhiteSpace(sobre.EventoId))
        {
            throw new ArgumentException(
                "El sobre tiene que llevar el identificador del evento.",
                nameof(sobre));
        }

        return telemetria.Empezar(operacion, contexto, sobre.EventoId, tipo);
    }

    private static void Escribir(object? portador, string clave, string? valor)
    {
        if (portador is Dictionary<string, string> destino && valor is not null)
        {
            destino[clave] = valor;
        }
    }
}
