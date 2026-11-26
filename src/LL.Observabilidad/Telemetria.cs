using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LL.Observabilidad;

/// <summary>
/// Fuente única de trazas y métricas de la cola de eventos. Declara el
/// <see cref="ActivitySource"/> y el <see cref="Meter"/> con el mismo nombre y
/// los contadores del contrato; los consumidores no crean instrumentos ni
/// nombres sueltos. Los tramos hijos son opcionales y no forman parte del
/// contrato de la cola.
/// </summary>
public sealed class Telemetria : IDisposable
{
    /// <summary>
    /// Tipo común de las actividades de cierre (ack y nack) y del valor por
    /// defecto de <see cref="Propagacion.Continuar"/>.
    /// </summary>
    public const ActivityKind TipoDeCierre = ActivityKind.Consumer;

    public Telemetria(string nombre = Nombres.Fuente)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        Nombre = nombre.Trim();
        Actividades = new ActivitySource(Nombre);
        Medidor = new Meter(Nombre);
        Publicados = Medidor.CreateCounter<long>(Nombres.Publicados);
        Reclamados = Medidor.CreateCounter<long>(Nombres.Reclamados);
        Aplicados = Medidor.CreateCounter<long>(Nombres.Aplicados);
        Fallidos = Medidor.CreateCounter<long>(Nombres.Fallidos);
        Muertos = Medidor.CreateCounter<long>(Nombres.Muertos);
    }

    /// <summary>Nombre común de la fuente y del medidor.</summary>
    public string Nombre { get; }

    /// <summary>Fuente de actividades que escucha el colector.</summary>
    public ActivitySource Actividades { get; }

    /// <summary>Medidor que agrupa los contadores del contrato.</summary>
    public Meter Medidor { get; }

    /// <summary>Eventos publicados y confirmados.</summary>
    public Counter<long> Publicados { get; }

    /// <summary>Eventos reclamados con lease concedido.</summary>
    public Counter<long> Reclamados { get; }

    /// <summary>Eventos aplicados y cerrados con ack.</summary>
    public Counter<long> Aplicados { get; }

    /// <summary>Eventos rechazados con nack.</summary>
    public Counter<long> Fallidos { get; }

    /// <summary>Eventos apartados en muerto.</summary>
    public Counter<long> Muertos { get; }

    /// <summary>Abre una actividad sin etiquetas propias.</summary>
    public Activity? Empezar(string operacion, ActivityKind tipo = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operacion);

        return Actividades.StartActivity(operacion, tipo);
    }

    /// <summary>Abre una actividad y la etiqueta con el identificador del evento.</summary>
    public Activity? Empezar(
        string operacion,
        string? eventoId,
        ActivityKind tipo = ActivityKind.Internal)
    {
        var actividad = Empezar(operacion, tipo);
        Etiquetar(actividad, Nombres.EtiquetaEvento, eventoId);

        return actividad;
    }

    /// <summary>
    /// Abre una actividad hija de un contexto recibido de otro proceso, con la
    /// etiqueta del evento que viaja en el sobre.
    /// </summary>
    public Activity? Empezar(
        string operacion,
        ActivityContext padre,
        string? eventoId,
        ActivityKind tipo = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operacion);

        var actividad = Actividades.StartActivity(operacion, tipo, padre);
        Etiquetar(actividad, Nombres.EtiquetaEvento, eventoId);

        return actividad;
    }

    /// <summary>Abre la actividad de publicación, que produce el evento.</summary>
    public Activity? Publicar(string eventoId, string? tipo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

        var actividad = Empezar(Nombres.Publicar, eventoId, ActivityKind.Producer);
        Etiquetar(actividad, Nombres.EtiquetaTipo, tipo);

        return actividad;
    }

    /// <summary>Abre la actividad de reclamo, que consume la entrega.</summary>
    public Activity? Reclamar(string eventoId, string? tipo = null, int? intentos = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

        var actividad = Empezar(Nombres.Reclamar, eventoId, ActivityKind.Consumer);
        Etiquetar(actividad, Nombres.EtiquetaTipo, tipo);

        if (intentos is not null)
        {
            Etiquetar(actividad, Nombres.EtiquetaIntentos, intentos.Value);
        }

        return actividad;
    }

    /// <summary>Abre la actividad de cierre con ack.</summary>
    public Activity? Ack(string eventoId, int? intentos = null)
    {
        return Cierre(Nombres.Ack, eventoId, intentos);
    }

    /// <summary>Abre la actividad de rechazo con nack.</summary>
    public Activity? Nack(string eventoId, int? intentos = null)
    {
        return Cierre(Nombres.Nack, eventoId, intentos);
    }

    /// <summary>Abre el tramo hijo opcional de aplicación del evento.</summary>
    public Activity? Aplicar(string? eventoId = null)
    {
        return Empezar(Nombres.Aplicar, eventoId);
    }

    /// <summary>Abre el tramo hijo opcional de exportación del resultado.</summary>
    public Activity? Exportar(string? eventoId = null)
    {
        return Empezar(Nombres.Exportar, eventoId);
    }

    /// <summary>Abre el tramo hijo opcional de comprobación del resultado.</summary>
    public Activity? Comprobar(string? eventoId = null)
    {
        return Empezar(Nombres.Comprobar, eventoId);
    }

    /// <summary>Abre el tramo hijo opcional del commit del cambio.</summary>
    public Activity? Commitear(string? eventoId = null)
    {
        return Empezar(Nombres.Commitear, eventoId);
    }

    /// <summary>Etiqueta el estado confirmado de una actividad ya abierta.</summary>
    public static void EtiquetarEstado(Activity? actividad, string? estado)
    {
        Etiquetar(actividad, Nombres.EtiquetaEstado, estado);
    }

    /// <summary>Etiqueta un valor de texto sin añadir etiquetas vacías.</summary>
    public static void Etiquetar(Activity? actividad, string clave, string? valor)
    {
        if (actividad is not null && !string.IsNullOrWhiteSpace(valor))
        {
            actividad.SetTag(clave, valor);
        }
    }

    /// <summary>Etiqueta un valor numérico.</summary>
    public static void Etiquetar(Activity? actividad, string clave, int valor)
    {
        actividad?.SetTag(clave, valor);
    }

    public void Dispose()
    {
        Actividades.Dispose();
        Medidor.Dispose();
    }

    private Activity? Cierre(string operacion, string eventoId, int? intentos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

        var actividad = Empezar(operacion, eventoId, TipoDeCierre);

        if (intentos is not null)
        {
            Etiquetar(actividad, Nombres.EtiquetaIntentos, intentos.Value);
        }

        return actividad;
    }
}
