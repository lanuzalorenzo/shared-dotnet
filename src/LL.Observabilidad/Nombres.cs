namespace LL.Observabilidad;

/// <summary>
/// Nombres de la fuente, las etiquetas, las actividades y los contadores.
/// El contrato de la cola fija la fuente <c>EventosApi</c>, la etiqueta
/// <c>evento.id</c> y los cinco contadores. Las actividades reproducen las
/// operaciones del servicio; los tramos hijos son nombres de conveniencia
/// para el consumidor, no obligaciones del paquete.
/// </summary>
public static class Nombres
{
    /// <summary>Fuente compartida del contrato de la cola de eventos.</summary>
    public const string Fuente = "EventosApi";

    /// <summary>Etiqueta con el identificador del evento.</summary>
    public const string EtiquetaEvento = "evento.id";

    /// <summary>Etiqueta con el tipo del evento.</summary>
    public const string EtiquetaTipo = "evento.tipo";

    /// <summary>Etiqueta con el estado confirmado del evento.</summary>
    public const string EtiquetaEstado = "evento.estado";

    /// <summary>Etiqueta con el número de entrega del lease.</summary>
    public const string EtiquetaIntentos = "intentos";

    /// <summary>Actividad de publicación de un evento.</summary>
    public const string Publicar = "eventos.publicar";

    /// <summary>Actividad de reclamo de una entrega.</summary>
    public const string Reclamar = "eventos.reclamar";

    /// <summary>Actividad de cierre con ack.</summary>
    public const string Ack = "eventos.ack";

    /// <summary>Actividad de rechazo con nack.</summary>
    public const string Nack = "eventos.nack";

    /// <summary>
    /// Tramo hijo de aplicación, opcional: describe una fase del consumidor y
    /// no forma parte del contrato de la cola.
    /// </summary>
    public const string Aplicar = "evento.aplicar";

    /// <summary>
    /// Tramo hijo de exportación, opcional: describe una fase del consumidor y
    /// no forma parte del contrato de la cola.
    /// </summary>
    public const string Exportar = "evento.exportar";

    /// <summary>
    /// Tramo hijo de comprobación, opcional: describe una fase del consumidor y
    /// no forma parte del contrato de la cola.
    /// </summary>
    public const string Comprobar = "evento.comprobar";

    /// <summary>
    /// Tramo hijo de commit, opcional: describe una fase del consumidor y no
    /// forma parte del contrato de la cola.
    /// </summary>
    public const string Commitear = "evento.commitear";

    /// <summary>Contador de eventos publicados.</summary>
    public const string Publicados = "eventos.publicados";

    /// <summary>Contador de eventos reclamados.</summary>
    public const string Reclamados = "eventos.reclamados";

    /// <summary>Contador de eventos aplicados y cerrados con ack.</summary>
    public const string Aplicados = "eventos.aplicados";

    /// <summary>Contador de eventos rechazados con nack.</summary>
    public const string Fallidos = "eventos.fallidos";

    /// <summary>Contador de eventos apartados en muerto.</summary>
    public const string Muertos = "eventos.muertos";
}
