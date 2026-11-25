namespace LL.Eventos.Client;

/// <summary>Opciones del cliente: solo afectan a la publicación, que es idempotente.</summary>
public sealed class EventosClientOptions
{
    /// <summary>Reintentos adicionales ante un fallo transitorio. Cero los desactiva.</summary>
    public int Reintentos { get; set; } = 2;

    /// <summary>Espera base del retroceso exponencial; se duplica en cada intento.</summary>
    public TimeSpan EsperaBase { get; set; } = TimeSpan.FromMilliseconds(200);

    internal void Validar()
    {
        if (Reintentos < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Reintentos), "No puede ser negativo.");
        }

        if (EsperaBase < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(EsperaBase), "No puede ser negativa.");
        }
    }
}
