using Microsoft.Extensions.Logging;

namespace LL.Observabilidad;

/// <summary>
/// Líneas de las transiciones del evento. Solo recibe el identificador, el
/// tipo, el intento y el tipo de error: no hay parámetro para la carga, así que
/// no puede acabar en el registro por descuido. El mensaje de una excepción
/// tampoco se escribe, porque puede contenerla.
/// </summary>
public sealed class RegistroDeEventos
{
    private const string SinTipo = "sin tipo";

    private readonly ILogger<RegistroDeEventos> _registro;

    public RegistroDeEventos(ILogger<RegistroDeEventos> registro)
    {
        ArgumentNullException.ThrowIfNull(registro);

        _registro = registro;
    }

    /// <summary>
    /// Abre un alcance con el identificador del evento. No añade la carga al
    /// estado del alcance.
    /// </summary>
    public IDisposable? Alcance(string eventoId)
    {
        return _registro.BeginScope(new Dictionary<string, object?>
        {
            [Nombres.EtiquetaEvento] = Validar(eventoId),
        });
    }

    /// <summary>Evento publicado y confirmado por la cola.</summary>
    public void Publicado(string eventoId, string? tipo = null)
    {
        _registro.LogInformation(
            "Evento {Evento} publicado: {Tipo}.",
            Validar(eventoId),
            tipo ?? SinTipo);
    }

    /// <summary>Entrega reclamada con su lease.</summary>
    public void Reclamado(string eventoId, string? tipo = null, int? intentos = null)
    {
        _registro.LogInformation(
            "Evento {Evento} reclamado: {Tipo} (intento {Intento}).",
            Validar(eventoId),
            tipo ?? SinTipo,
            intentos);
    }

    /// <summary>Evento aplicado y cerrado con ack.</summary>
    public void Aplicado(string eventoId, string? tipo = null)
    {
        _registro.LogInformation(
            "Evento {Evento} aplicado: {Tipo}.",
            Validar(eventoId),
            tipo ?? SinTipo);
    }

    /// <summary>
    /// Evento rechazado con nack. Solo escribe el tipo de la excepción, nunca
    /// su mensaje, que puede copiar la carga.
    /// </summary>
    public void Fallido(string eventoId, Exception error, string? tipo = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        _registro.LogWarning(
            "Evento {Evento} fallido: {Tipo}; {Error}.",
            Validar(eventoId),
            tipo ?? SinTipo,
            error.GetType().Name);
    }

    /// <summary>Evento apartado en muerto después de agotar los intentos.</summary>
    public void Muerto(string eventoId, string? tipo = null, int? intentos = null)
    {
        _registro.LogWarning(
            "Evento {Evento} apartado en muerto: {Tipo} (intento {Intento}).",
            Validar(eventoId),
            tipo ?? SinTipo,
            intentos);
    }

    private static string Validar(string eventoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventoId);

        return eventoId;
    }
}
