using System.Text.Json;

namespace LL.Eventos.Client;

/// <summary>Evento tal como lo devuelve el contrato v1, sin tipos del dominio.</summary>
public sealed record Evento(
    string Id,
    string Tipo,
    JsonElement Carga,
    DateTimeOffset PublicadoEn,
    string Estado,
    int Intentos,
    DateTimeOffset? LeaseHasta,
    JsonElement? Resultado);

/// <summary>Cierre de una entrega con el hash del commit que la aplicó.</summary>
public sealed record Acuse(string Hash, int Intentos);

/// <summary>Cierre fallido de una entrega con el motivo del rechazo.</summary>
public sealed record Rechazo(string Motivo, int Intentos);
