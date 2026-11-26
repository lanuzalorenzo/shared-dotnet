namespace LL.Eventos.Worker;

/// <summary>Foto de la cola vista por el worker en un instante.</summary>
/// <param name="Atascada">Hay una entrega en curso que ya no se puede cerrar a tiempo.</param>
/// <param name="EnCurso">Entregas abiertas en este proceso.</param>
/// <param name="MasAntigua">Edad de la entrega abierta más antigua.</param>
/// <param name="UltimoCierre">Último cierre, con ack o con nack.</param>
/// <param name="EventoAtascado">Identificador de la entrega que dispara el atasco.</param>
public sealed record EstadoDeLaCola(
    bool Atascada,
    int EnCurso,
    TimeSpan? MasAntigua,
    DateTimeOffset? UltimoCierre,
    string? EventoAtascado);

/// <summary>
/// Sonda del atasco local: una entrega abierta cuyo lease ya venció o, si el
/// contrato no manda lease, que supera el umbral. No mide atraso: una cola
/// vacía no es un atasco y no se convierte en fallo de salud.
///
/// La clave es (identificador, intento): el mismo evento puede reentregarse
/// con otro lease mientras el primer consumidor todavía lo tiene en la mano.
/// </summary>
public sealed class SondaDeCola
{
    private readonly object _cerrojo = new();
    private readonly Dictionary<(string Id, int Intento), Entrega> _enCurso = [];
    private readonly TimeSpan _umbral;
    private readonly TimeProvider _reloj;
    private DateTimeOffset? _ultimoCierre;

    public SondaDeCola(TimeSpan umbral, TimeProvider? reloj = null)
    {
        if (umbral <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(umbral), "Debe ser mayor que cero.");
        }

        _umbral = umbral;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <summary>Estado actual. Se calcula al leerlo, no se guarda.</summary>
    public EstadoDeLaCola Estado
    {
        get
        {
            var ahora = _reloj.GetUtcNow();

            lock (_cerrojo)
            {
                TimeSpan? masAntigua = null;
                string? atascado = null;

                foreach (var entrega in _enCurso.Values)
                {
                    var edad = ahora - entrega.Inicio;

                    if (masAntigua is null || edad > masAntigua)
                    {
                        masAntigua = edad;
                    }

                    var vencida = entrega.LeaseHasta is { } lease
                        ? ahora >= lease
                        : edad > _umbral;

                    if (atascado is null && vencida)
                    {
                        atascado = entrega.Id;
                    }
                }

                return new EstadoDeLaCola(
                    atascado is not null,
                    _enCurso.Count,
                    masAntigua,
                    _ultimoCierre,
                    atascado);
            }
        }
    }

    /// <summary>Abre una entrega; se cierra al liberar el objeto devuelto.</summary>
    public Entrega EnCurso(string id, int intento, DateTimeOffset? leaseHasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var entrega = new Entrega(this, id, intento, _reloj.GetUtcNow(), leaseHasta);

        lock (_cerrojo)
        {
            _enCurso[(id, intento)] = entrega;
        }

        return entrega;
    }

    private void Cerrar(Entrega entrega)
    {
        lock (_cerrojo)
        {
            var clave = (entrega.Id, entrega.Intento);

            // Solo se cierra la entrada que sigue siendo esta entrega.
            if (_enCurso.TryGetValue(clave, out var actual) && ReferenceEquals(actual, entrega))
            {
                _enCurso.Remove(clave);
            }

            _ultimoCierre = _reloj.GetUtcNow();
        }
    }

    /// <summary>Entrega abierta. Solo la sonda crea instancias.</summary>
    public sealed class Entrega : IDisposable
    {
        private readonly SondaDeCola _sonda;
        private bool _cerrada;

        internal Entrega(
            SondaDeCola sonda,
            string id,
            int intento,
            DateTimeOffset inicio,
            DateTimeOffset? leaseHasta)
        {
            _sonda = sonda;
            Id = id;
            Intento = intento;
            Inicio = inicio;
            LeaseHasta = leaseHasta;
        }

        public string Id { get; }

        public int Intento { get; }

        public DateTimeOffset Inicio { get; }

        public DateTimeOffset? LeaseHasta { get; }

        public void Dispose()
        {
            if (_cerrada)
            {
                return;
            }

            _cerrada = true;
            _sonda.Cerrar(this);
        }
    }
}
