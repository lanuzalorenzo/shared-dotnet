using System.Net;
using System.Text;
using System.Text.Json;

namespace LL.Eventos.Worker.Tests;

/// <summary>
/// Doble del contrato v1 con lease: estados, testigo de entrega, ack con hash,
/// nack con motivo y espera de reintento. Habla el JSON snake_case de la API,
/// no una abstracción intermedia.
/// </summary>
internal sealed class ColaDoble : HttpMessageHandler
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly object _cerrojo = new();
    private readonly List<EventoDoble> _eventos = [];
    private readonly Dictionary<string, EventoDoble> _porId = new(StringComparer.Ordinal);
    private int _siguiente;

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan EsperaDeReintento { get; set; } = TimeSpan.Zero;

    public List<AcuseDoble> Acuses { get; } = [];

    public List<RechazoDoble> Rechazos { get; } = [];

    public int Reclamos { get; private set; }

    public int ReclamosVacios { get; private set; }

    public Task? EsperaDeReclamo { get; set; }

    public bool IdVacioEnElProximoReclamo { get; set; }

    public string Publicar(string tipo, string carga = "{}")
    {
        lock (_cerrojo)
        {
            var id = Id(_siguiente++);
            var evento = new EventoDoble(id, tipo, carga, DateTimeOffset.UtcNow);
            _eventos.Add(evento);
            _porId[id] = evento;
            return id;
        }
    }

    public EventoDoble? EstadoDe(string id)
    {
        lock (_cerrojo)
        {
            return _porId.GetValueOrDefault(id)?.Copia();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion,
        CancellationToken cancelacion)
    {
        var cuerpo = peticion.Content is null
            ? ""
            : await peticion.Content.ReadAsStringAsync(cancelacion);

        var ruta = peticion.RequestUri!.AbsolutePath;

        if (peticion.Method == HttpMethod.Post && ruta == "/v1/eventos/siguiente")
        {
            if (EsperaDeReclamo is { } espera)
            {
                await espera.ConfigureAwait(false);
            }

            return Reclamar(Tipo(peticion.RequestUri));
        }

        var partes = ruta.Trim('/').Split('/');

        if (peticion.Method == HttpMethod.Post
            && partes.Length == 4
            && partes[0] == "v1"
            && partes[1] == "eventos")
        {
            return partes[3] switch
            {
                "ack" => Acusar(partes[2], cuerpo),
                "nack" => Rechazar(partes[2], cuerpo),
                _ => NoEncontrado(),
            };
        }

        return NoEncontrado();
    }

    private HttpResponseMessage Reclamar(string? tipo)
    {
        lock (_cerrojo)
        {
            var ahora = DateTimeOffset.UtcNow;

            var candidato = _eventos.FirstOrDefault(evento =>
                (tipo is null || evento.Tipo == tipo)
                && evento.Estado switch
                {
                    "pendiente" => true,
                    "fallido" => evento.DisponibleEn <= ahora,
                    "reclamado" => evento.LeaseHasta <= ahora,
                    _ => false,
                });

            if (candidato is null)
            {
                ReclamosVacios++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            candidato.Intentos++;
            candidato.Estado = "reclamado";
            candidato.LeaseHasta = ahora + Lease;
            candidato.DisponibleEn = null;
            Reclamos++;

            var evento = Evento(candidato);

            if (IdVacioEnElProximoReclamo)
            {
                IdVacioEnElProximoReclamo = false;
                evento["id"] = "";
            }

            return Json(HttpStatusCode.OK, evento);
        }
    }

    private HttpResponseMessage Acusar(string id, string cuerpo)
    {
        lock (_cerrojo)
        {
            if (!_porId.TryGetValue(id, out var evento))
            {
                return NoEncontrado();
            }

            using var documento = JsonDocument.Parse(cuerpo);
            var hash = documento.RootElement.GetProperty("hash").GetString() ?? "";
            var intentos = documento.RootElement.GetProperty("intentos").GetInt32();

            if (!Vigente(evento, intentos))
            {
                return Conflicto("La entrega no es la vigente.");
            }

            evento.Estado = "aceptado";
            evento.LeaseHasta = null;
            evento.Resultado = hash;
            Acuses.Add(new AcuseDoble(id, hash, intentos));

            return Json(HttpStatusCode.OK, Evento(evento));
        }
    }

    private HttpResponseMessage Rechazar(string id, string cuerpo)
    {
        lock (_cerrojo)
        {
            if (!_porId.TryGetValue(id, out var evento))
            {
                return NoEncontrado();
            }

            using var documento = JsonDocument.Parse(cuerpo);
            var motivo = documento.RootElement.GetProperty("motivo").GetString() ?? "";
            var intentos = documento.RootElement.GetProperty("intentos").GetInt32();

            if (!Vigente(evento, intentos))
            {
                return Conflicto("La entrega no es la vigente.");
            }

            evento.Estado = "fallido";
            evento.LeaseHasta = null;
            evento.DisponibleEn = DateTimeOffset.UtcNow + EsperaDeReintento;
            evento.Resultado = motivo;
            Rechazos.Add(new RechazoDoble(id, motivo, intentos));

            return Json(HttpStatusCode.OK, Evento(evento));
        }
    }

    private static bool Vigente(EventoDoble evento, int intentos)
    {
        return evento.Estado == "reclamado"
            && evento.Intentos == intentos
            && evento.LeaseHasta >= DateTimeOffset.UtcNow;
    }

    private static string? Tipo(Uri uri)
    {
        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = par.Split('=', 2);

            if (partes[0] == "tipo")
            {
                return Uri.UnescapeDataString(partes[1]);
            }
        }

        return null;
    }

    private static Dictionary<string, object?> Evento(EventoDoble evento)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = evento.Id,
            ["tipo"] = evento.Tipo,
            ["carga"] = JsonSerializer.Deserialize<JsonElement>(evento.Carga),
            ["publicado_en"] = evento.PublicadoEn,
            ["estado"] = evento.Estado,
            ["intentos"] = evento.Intentos,
            ["lease_hasta"] = evento.LeaseHasta,
            ["resultado"] = evento.Resultado is null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [evento.Estado == "aceptado" ? "hash" : "motivo"] = evento.Resultado,
                },
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode estado, object valor)
    {
        return new HttpResponseMessage(estado)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(valor),
                Encoding.UTF8,
                "application/json"),
        };
    }

    private static HttpResponseMessage Conflicto(string detalle)
    {
        return Json(
            HttpStatusCode.Conflict,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = detalle,
                ["status"] = 409,
            });
    }

    private static HttpResponseMessage NoEncontrado()
    {
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Id(int numero)
    {
        var caracteres = new char[26];

        for (var i = caracteres.Length - 1; i >= 0; i--)
        {
            caracteres[i] = Crockford[numero % Crockford.Length];
            numero /= Crockford.Length;
        }

        return $"evt_{new string(caracteres)}";
    }

    internal sealed record AcuseDoble(string Id, string Hash, int Intentos);

    internal sealed record RechazoDoble(string Id, string Motivo, int Intentos);

    internal sealed class EventoDoble(
        string id,
        string tipo,
        string carga,
        DateTimeOffset publicadoEn)
    {
        public string Id { get; } = id;

        public string Tipo { get; } = tipo;

        public string Carga { get; } = carga;

        public DateTimeOffset PublicadoEn { get; } = publicadoEn;

        public string Estado { get; set; } = "pendiente";

        public int Intentos { get; set; }

        public DateTimeOffset? LeaseHasta { get; set; }

        public DateTimeOffset? DisponibleEn { get; set; }

        public string? Resultado { get; set; }

        public EventoDoble Copia() =>
            new(Id, Tipo, Carga, PublicadoEn)
            {
                Estado = Estado,
                Intentos = Intentos,
                LeaseHasta = LeaseHasta,
                DisponibleEn = DisponibleEn,
                Resultado = Resultado,
            };
    }
}
