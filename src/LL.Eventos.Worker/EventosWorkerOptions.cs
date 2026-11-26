namespace LL.Eventos.Worker;

/// <summary>
/// Opciones del bucle de consumo. Los tiempos se eligen con el lease del
/// contrato en la cabeza: el plazo de apagado va por debajo y el umbral de
/// atasco, por encima, porque un lease vencido ya permite la reentrega.
/// </summary>
public sealed class EventosWorkerOptions
{
    /// <summary>Tipo de evento que se reclama; nulo o vacío reclama cualquiera.</summary>
    public string? Tipo { get; set; }

    /// <summary>Entregas en curso como máximo. Cada una es un bucle propio.</summary>
    public int ConcurrenciaMaxima { get; set; } = 4;

    /// <summary>Espera tras un reclamo sin trabajo antes de volver a preguntar.</summary>
    public TimeSpan EsperaSinTrabajo { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Espera tras un fallo de transporte antes de reintentar el reclamo.</summary>
    public TimeSpan EsperaTrasError { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Margen para terminar la entrega en curso cuando llega la parada.</summary>
    public TimeSpan TiempoDeApagado { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Edad de una entrega sin cerrar que la sonda marca como atasco.</summary>
    public TimeSpan UmbralDeAtasco { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validar()
    {
        Tipo = string.IsNullOrWhiteSpace(Tipo) ? null : Tipo.Trim();

        if (ConcurrenciaMaxima is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConcurrenciaMaxima),
                "Debe estar entre 1 y 64.");
        }

        if (EsperaSinTrabajo < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(EsperaSinTrabajo), "No puede ser negativa.");
        }

        if (EsperaTrasError < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(EsperaTrasError), "No puede ser negativa.");
        }

        if (TiempoDeApagado <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TiempoDeApagado), "Debe ser mayor que cero.");
        }

        if (UmbralDeAtasco <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UmbralDeAtasco), "Debe ser mayor que cero.");
        }
    }
}
