using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LL.Eventos.Client.Tests;

public sealed class EventosClientTest
{
    private const string UnEvento = """
        {
          "id": "evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD",
          "tipo": "pedido.creado",
          "carga": { "pedido": 42 },
          "publicado_en": "2026-11-25T10:00:00+00:00",
          "estado": "pendiente",
          "intentos": 0,
          "lease_hasta": null,
          "resultado": null
        }
        """;

    private static (EventosClient Cliente, DobleHttp Doble) Crear(int reintentos = 0)
    {
        var doble = new DobleHttp();
        var http = new HttpClient(doble) { BaseAddress = new Uri("https://eventos.test/") };
        var opciones = new EventosClientOptions
        {
            Reintentos = reintentos,
            EsperaBase = TimeSpan.Zero,
        };

        return (new EventosClient(http, opciones), doble);
    }

    [Fact]
    public async Task Publish_sends_the_contract_envelope_and_returns_the_event()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.Created, UnEvento);
        using var carga = JsonDocument.Parse("""{ "orden": 2, "texto": "ñ" }""");

        var evento = await cliente.PublishAsync("pedido.creado", carga.RootElement.Clone());

        var peticion = Assert.Single(doble.Peticiones);
        Assert.Equal(HttpMethod.Post, peticion.Metodo);
        Assert.Equal("https://eventos.test/v1/eventos", peticion.Ruta);

        using var enviado = JsonDocument.Parse(peticion.Cuerpo);
        var raiz = enviado.RootElement;
        Assert.Equal("pedido.creado", raiz.GetProperty("tipo").GetString());
        Assert.Equal(2, raiz.GetProperty("carga").GetProperty("orden").GetInt32());
        Assert.Equal("ñ", raiz.GetProperty("carga").GetProperty("texto").GetString());
        Assert.Matches("^evt_[0-9A-HJKMNP-TV-Z]{26}$", raiz.GetProperty("id").GetString());

        Assert.Equal("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD", evento.Id);
        Assert.Equal("pedido.creado", evento.Tipo);
        Assert.Equal("pendiente", evento.Estado);
        Assert.Equal(42, evento.Carga.GetProperty("pedido").GetInt32());
        Assert.Equal(
            DateTimeOffset.Parse("2026-11-25T10:00:00+00:00", CultureInfo.InvariantCulture),
            evento.PublicadoEn);
        Assert.Null(evento.LeaseHasta);
    }

    [Fact]
    public async Task Claim_reads_the_snake_case_fields_of_the_contract()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.OK, """
            {
              "id": "evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD",
              "tipo": "pedido.creado",
              "carga": { "pedido": 42 },
              "publicado_en": "2026-11-25T10:00:00+00:00",
              "estado": "reclamado",
              "intentos": 1,
              "lease_hasta": "2026-11-25T10:05:00+00:00",
              "resultado": null
            }
            """);

        var evento = await cliente.ClaimAsync("pedido.creado");

        Assert.NotNull(evento);
        Assert.Equal(1, evento.Intentos);
        Assert.Equal(
            DateTimeOffset.Parse("2026-11-25T10:05:00+00:00", CultureInfo.InvariantCulture),
            evento.LeaseHasta);
    }

    [Fact]
    public async Task Publish_retries_with_the_same_identifier()
    {
        var (cliente, doble) = Crear(reintentos: 1);
        var id = EventoId.Nuevo();
        doble.Encolar(HttpStatusCode.ServiceUnavailable);
        doble.Encolar(HttpStatusCode.Created, UnEvento);

        await cliente.PublishAsync("pedido.creado", JsonDocument.Parse("{}").RootElement.Clone(), id);

        Assert.Equal(2, doble.Peticiones.Count);
        Assert.All(doble.Peticiones, peticion => Assert.Contains($"\"id\":\"{id}\"", peticion.Cuerpo));
    }

    [Fact]
    public async Task Publish_keeps_the_payload_conventions_of_the_consumer()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.Created, UnEvento);

        await cliente.PublishAsync("pedido.creado", new CargaDePrueba(7));

        using var enviado = JsonDocument.Parse(Assert.Single(doble.Peticiones).Cuerpo);
        var carga = enviado.RootElement.GetProperty("carga");
        Assert.Equal(7, carga.GetProperty("pedidoId").GetInt32());
        Assert.False(carga.TryGetProperty("pedido_id", out _));
    }

    [Fact]
    public async Task Publish_rejects_an_identifier_outside_the_contract()
    {
        var (cliente, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => cliente.PublishAsync("pedido.creado", JsonDocument.Parse("{}").RootElement.Clone(), "42"));
    }

    [Fact]
    public async Task Publish_maps_the_problem_status_and_detail()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(
            HttpStatusCode.BadRequest,
            """{ "title": "Solicitud inválida", "detail": "El tipo no es válido." }""");

        var excepcion = await Assert.ThrowsAsync<EventosClientException>(
            () => cliente.PublishAsync("no valido", JsonDocument.Parse("{}").RootElement.Clone()));

        Assert.Equal(HttpStatusCode.BadRequest, excepcion.Estado);
        Assert.Equal("El tipo no es válido.", excepcion.Detalle);
    }

    [Fact]
    public async Task Publish_wraps_a_transport_failure_in_the_client_exception()
    {
        var (cliente, doble) = Crear();
        doble.EncolarFallo(new HttpRequestException("sin red"));

        var excepcion = await Assert.ThrowsAsync<EventosClientException>(
            () => cliente.PublishAsync("pedido.creado", JsonDocument.Parse("{}").RootElement.Clone()));

        Assert.Null(excepcion.Estado);
        Assert.Contains("sin red", excepcion.Message);
    }

    [Fact]
    public async Task Publish_retries_after_a_transport_failure_with_the_same_identifier()
    {
        var (cliente, doble) = Crear(reintentos: 1);
        var id = EventoId.Nuevo();
        doble.EncolarFallo(new HttpRequestException("sin red"));
        doble.Encolar(HttpStatusCode.Created, UnEvento);

        await cliente.PublishAsync("pedido.creado", JsonDocument.Parse("{}").RootElement.Clone(), id);

        Assert.Equal(2, doble.Peticiones.Count);
        Assert.All(doble.Peticiones, peticion => Assert.Contains($"\"id\":\"{id}\"", peticion.Cuerpo));
    }

    [Fact]
    public async Task Claim_wraps_a_transport_failure_in_the_client_exception()
    {
        var (cliente, doble) = Crear();
        doble.EncolarFallo(new TaskCanceledException("tiempo agotado"));

        var excepcion = await Assert.ThrowsAsync<EventosClientException>(() => cliente.ClaimAsync());

        Assert.Null(excepcion.Estado);
        Assert.Contains("tiempo agotado", excepcion.Message);
    }

    [Fact]
    public async Task Publish_maps_a_success_with_invalid_json_to_the_client_exception()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.Created, "no-json");

        var excepcion = await Assert.ThrowsAsync<EventosClientException>(
            () => cliente.PublishAsync("pedido.creado", JsonDocument.Parse("{}").RootElement.Clone()));

        Assert.Equal(HttpStatusCode.Created, excepcion.Estado);
        Assert.IsType<JsonException>(excepcion.InnerException);
    }

    [Fact]
    public async Task Claim_returns_null_and_filters_by_type()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.NoContent);

        var evento = await cliente.ClaimAsync("pedido.creado");

        Assert.Null(evento);
        var peticion = Assert.Single(doble.Peticiones);
        Assert.Equal(HttpMethod.Post, peticion.Metodo);
        Assert.Equal("https://eventos.test/v1/eventos/siguiente?tipo=pedido.creado", peticion.Ruta);
        Assert.Equal("", peticion.Cuerpo);
    }

    [Fact]
    public async Task Claim_without_type_adds_no_query()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.NoContent);

        await cliente.ClaimAsync();

        Assert.Equal("https://eventos.test/v1/eventos/siguiente", Assert.Single(doble.Peticiones).Ruta);
    }

    [Fact]
    public async Task Ack_posts_hash_and_attempts_to_the_event_route()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.OK, UnEvento);

        var evento = await cliente.AckAsync("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD", "abc123", 1);

        var peticion = Assert.Single(doble.Peticiones);
        Assert.Equal(
            "https://eventos.test/v1/eventos/evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD/ack",
            peticion.Ruta);
        Assert.Contains("\"hash\":\"abc123\"", peticion.Cuerpo);
        Assert.Contains("\"intentos\":1", peticion.Cuerpo);
        Assert.Equal("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD", evento.Id);
    }

    [Fact]
    public async Task Nack_posts_reason_and_attempts_to_the_event_route()
    {
        var (cliente, doble) = Crear();
        doble.Encolar(HttpStatusCode.OK, UnEvento);

        await cliente.NackAsync("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD", "dependencia caída", 2);

        var peticion = Assert.Single(doble.Peticiones);
        Assert.Equal(
            "https://eventos.test/v1/eventos/evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD/nack",
            peticion.Ruta);

        using var enviado = JsonDocument.Parse(peticion.Cuerpo);
        Assert.Equal("dependencia caída", enviado.RootElement.GetProperty("motivo").GetString());
        Assert.Equal(2, enviado.RootElement.GetProperty("intentos").GetInt32());
    }

    private sealed record CargaDePrueba(int PedidoId);
}

public sealed class EventoIdTest
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void New_matches_the_contract_shape()
    {
        var id = EventoId.Nuevo();

        Assert.Matches("^evt_[0-9A-HJKMNP-TV-Z]{26}$", id);
        Assert.True(EventoId.EsValido(id));
    }

    [Fact]
    public void New_encodes_the_time_in_the_first_48_bits()
    {
        var momento = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123);

        var id = EventoId.Nuevo(momento);

        Assert.Equal(momento.ToUnixTimeMilliseconds(), Tiempo(id));
    }

    [Fact]
    public void Two_identifiers_in_a_row_differ()
    {
        Assert.NotEqual(EventoId.Nuevo(), EventoId.Nuevo());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1C")]
    [InlineData("EVT_01J9Z6Q3Y8W4T6P2M0K5N7R1CD")]
    [InlineData("evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CU")]
    [InlineData("evt_01j9z6q3y8w4t6p2m0k5n7r1cd")]
    [InlineData("evt_81J9Z6Q3Y8W4T6P2M0K5N7R1CD")]
    public void IsValid_rejects_what_is_not_a_ulid(string? id)
    {
        Assert.False(EventoId.EsValido(id));
    }

    private static long Tiempo(string id)
    {
        ulong valor = 0;

        foreach (var caracter in id.AsSpan(4, 10))
        {
            valor = (valor << 5) | (uint)Crockford.IndexOf(caracter);
        }

        return (long)(valor & 0xFFFF_FFFF_FFFF);
    }
}
