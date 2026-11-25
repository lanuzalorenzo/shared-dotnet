using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LL.Eventos.Client;

/// <summary>
/// Cliente del contrato v1 de la cola de eventos. No conoce el dominio: recibe
/// la carga como <see cref="JsonElement"/> y devuelve el evento del contrato.
///
/// Solo la publicación se reintenta: su identificador es la clave de
/// idempotencia y repetirla no crea otro evento. Reclamar, ack y nack cambian
/// estado y un reintento ciego podría duplicar el lease o cerrar dos veces.
/// </summary>
public sealed class EventosClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly EventosClientOptions _opciones;

    public EventosClient(HttpClient cliente, EventosClientOptions? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        if (cliente.BaseAddress is null)
        {
            throw new ArgumentException(
                "El HttpClient necesita BaseAddress apuntando a la API de eventos.",
                nameof(cliente));
        }

        _http = cliente;
        _opciones = opciones ?? new EventosClientOptions();
        _opciones.Validar();
    }

    /// <summary>
    /// Publica un evento con la carga tal como llega. Sin identificador se
    /// genera uno ULID; con identificador, repetir la llamada es idempotente.
    /// </summary>
    public async Task<Evento> PublishAsync(
        string tipo,
        JsonElement carga,
        string? id = null,
        CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);

        if (id is not null && !EventoId.EsValido(id))
        {
            throw new ArgumentException(
                "El identificador debe ser un ULID con el prefijo evt_ y 26 caracteres.",
                nameof(id));
        }

        var sobre = new SobrePublicacion(id ?? EventoId.Nuevo(), tipo, carga);

        using var respuesta = await EnviarAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/eventos")
            {
                Content = JsonContent.Create(sobre, options: Json),
            },
            reintentar: true,
            cancelacion);

        return await LeerEventoAsync(respuesta, cancelacion);
    }

    /// <summary>Publica un objeto serializado con las convenciones del contrato.</summary>
    public Task<Evento> PublishAsync<T>(
        string tipo,
        T carga,
        string? id = null,
        CancellationToken cancelacion = default)
    {
        return PublishAsync(tipo, JsonSerializer.SerializeToElement(carga, Json), id, cancelacion);
    }

    /// <summary>Reclama el siguiente evento, opcionalmente de un tipo. Nulo si no hay.</summary>
    public async Task<Evento?> ClaimAsync(
        string? tipo = null,
        CancellationToken cancelacion = default)
    {
        var ruta = tipo is null or ""
            ? "v1/eventos/siguiente"
            : $"v1/eventos/siguiente?tipo={Uri.EscapeDataString(tipo)}";

        using var respuesta = await EnviarAsync(
            () => new HttpRequestMessage(HttpMethod.Post, ruta),
            reintentar: false,
            cancelacion);

        if (respuesta.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        return await LeerEventoAsync(respuesta, cancelacion);
    }

    /// <summary>Cierra la entrega con el hash del commit y el número de intento.</summary>
    public async Task<Evento> AckAsync(
        string id,
        string hash,
        int intentos,
        CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var cuerpo = new Acuse(hash, intentos);

        return await EnviarCierreAsync(id, "ack", cuerpo, cancelacion);
    }

    /// <summary>Rechaza la entrega con el motivo y el número de intento.</summary>
    public async Task<Evento> NackAsync(
        string id,
        string motivo,
        int intentos,
        CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);

        var cuerpo = new Rechazo(motivo, intentos);

        return await EnviarCierreAsync(id, "nack", cuerpo, cancelacion);
    }

    private async Task<Evento> EnviarCierreAsync(
        string id,
        string accion,
        object cuerpo,
        CancellationToken cancelacion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var ruta = $"v1/eventos/{Uri.EscapeDataString(id)}/{accion}";
        using var respuesta = await EnviarAsync(
            () => new HttpRequestMessage(HttpMethod.Post, ruta)
            {
                Content = JsonContent.Create(cuerpo, options: Json),
            },
            reintentar: false,
            cancelacion);

        return await LeerEventoAsync(respuesta, cancelacion);
    }

    private async Task<HttpResponseMessage> EnviarAsync(
        Func<HttpRequestMessage> crear,
        bool reintentar,
        CancellationToken cancelacion)
    {
        for (var intento = 0; ; intento++)
        {
            using var peticion = crear();

            try
            {
                var respuesta = await _http.SendAsync(peticion, cancelacion);

                if (reintentar
                    && EsTransitorio(respuesta.StatusCode)
                    && intento < _opciones.Reintentos)
                {
                    respuesta.Dispose();
                    await EsperarAsync(intento, cancelacion);
                    continue;
                }

                return respuesta;
            }
            catch (Exception excepcion) when (
                (excepcion is HttpRequestException or TaskCanceledException)
                && !cancelacion.IsCancellationRequested)
            {
                if (reintentar && intento < _opciones.Reintentos)
                {
                    await EsperarAsync(intento, cancelacion);
                    continue;
                }

                throw new EventosClientException(null, excepcion.Message, excepcion);
            }
        }
    }

    private static async Task<Evento> LeerEventoAsync(
        HttpResponseMessage respuesta,
        CancellationToken cancelacion)
    {
        if (!respuesta.IsSuccessStatusCode)
        {
            throw await ExcepcionAsync(respuesta, cancelacion);
        }

        var evento = await LeerJsonAsync<Evento>(respuesta, cancelacion);

        return evento ?? throw new EventosClientException(
            respuesta.StatusCode,
            "La respuesta no trae el evento.");
    }

    private static async Task<T?> LeerJsonAsync<T>(
        HttpResponseMessage respuesta,
        CancellationToken cancelacion)
    {
        try
        {
            return await respuesta.Content.ReadFromJsonAsync<T>(Json, cancelacion);
        }
        catch (JsonException excepcion)
        {
            throw new EventosClientException(
                respuesta.StatusCode,
                "La respuesta no es JSON del contrato.",
                excepcion);
        }
    }

    private static async Task<EventosClientException> ExcepcionAsync(
        HttpResponseMessage respuesta,
        CancellationToken cancelacion)
    {
        string? detalle = null;

        try
        {
            using var documento = await JsonDocument.ParseAsync(
                await respuesta.Content.ReadAsStreamAsync(cancelacion),
                cancellationToken: cancelacion);

            detalle = Texto(documento.RootElement, "detail") ?? Texto(documento.RootElement, "title");
        }
        catch (JsonException)
        {
            // Sin ProblemDetails: el estado basta para diagnosticar.
        }

        return new EventosClientException(respuesta.StatusCode, detalle);
    }

    private static string? Texto(JsonElement raiz, string propiedad)
    {
        return raiz.TryGetProperty(propiedad, out var valor)
            && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
    }

    private static bool EsTransitorio(HttpStatusCode estado)
    {
        return estado is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)estado >= 500;
    }

    private Task EsperarAsync(int intento, CancellationToken cancelacion)
    {
        var ticks = (long)(_opciones.EsperaBase.Ticks * Math.Pow(2, intento));
        return Task.Delay(TimeSpan.FromTicks(ticks), cancelacion);
    }

    private sealed record SobrePublicacion(string Id, string Tipo, JsonElement Carga);
}
