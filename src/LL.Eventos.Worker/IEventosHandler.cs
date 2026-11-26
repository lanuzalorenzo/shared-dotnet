using LL.Eventos.Client;

namespace LL.Eventos.Worker;

/// <summary>
/// Aplica un evento y devuelve el hash del commit que lo confirma. El worker
/// cierra con ese hash y con el número de entrega del propio evento; el
/// manejador no los inventa ni conoce el lease.
/// </summary>
public interface IEventosHandler
{
    /// <summary>
    /// Aplica el evento. Un retorno vacío o una excepción se rechazan con nack;
    /// el manejador debe tolerar que el mismo evento llegue más de una vez.
    /// </summary>
    Task<string> HandleAsync(Evento evento, CancellationToken cancelacion);
}
