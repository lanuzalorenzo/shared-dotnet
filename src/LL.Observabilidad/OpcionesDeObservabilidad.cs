namespace LL.Observabilidad;

/// <summary>
/// Opciones de la instrumentación. El nombre de la fuente por defecto es el
/// del contrato de la cola de eventos; una aplicación que quiera otra fuente
/// lo cambia aquí y configura su colector con ese nombre.
/// </summary>
public sealed class OpcionesDeObservabilidad
{
    /// <summary>Nombre del <c>ActivitySource</c> y del <c>Meter</c>.</summary>
    public string NombreDeLaFuente { get; set; } = Nombres.Fuente;

    internal void Validar()
    {
        if (string.IsNullOrWhiteSpace(NombreDeLaFuente))
        {
            throw new ArgumentException(
                "El nombre de la fuente no puede estar vacío.",
                nameof(NombreDeLaFuente));
        }

        NombreDeLaFuente = NombreDeLaFuente.Trim();
    }
}
