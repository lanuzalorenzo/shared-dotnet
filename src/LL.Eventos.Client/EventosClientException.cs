using System.Net;

namespace LL.Eventos.Client;

/// <summary>La API no devolvió un resultado del contrato o falló el transporte.</summary>
public sealed class EventosClientException : Exception
{
    public EventosClientException(HttpStatusCode? estado, string? detalle)
        : this(estado, detalle, null)
    {
    }

    public EventosClientException(HttpStatusCode? estado, string? detalle, Exception? interna)
        : base(
            estado is null
                ? "No se pudo completar la petición." + (detalle is null ? "" : $" {detalle}")
                : $"La API de eventos respondió {(int)estado} {estado}." + (detalle is null ? "" : $" {detalle}"),
            interna)
    {
        Estado = estado;
        Detalle = detalle;
    }

    /// <summary>Estado HTTP de la respuesta, o nulo si falló el transporte.</summary>
    public HttpStatusCode? Estado { get; }

    /// <summary>Detalle del problema o del fallo de transporte, si lo hay.</summary>
    public string? Detalle { get; }
}
