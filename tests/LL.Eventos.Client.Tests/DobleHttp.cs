using System.Net;
using System.Text;

namespace LL.Eventos.Client.Tests;

/// <summary>Doble del contrato: guarda la petición y devuelve lo que se encole.</summary>
internal sealed class DobleHttp : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _respuestas = new();

    public List<Peticion> Peticiones { get; } = [];

    public void Encolar(HttpStatusCode estado, string? json = null)
    {
        _respuestas.Enqueue(() => new HttpResponseMessage(estado)
        {
            Content = json is null
                ? null
                : new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    public void EncolarFallo(Exception error)
    {
        _respuestas.Enqueue(() => throw error);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion,
        CancellationToken cancelacion)
    {
        var cuerpo = peticion.Content is null
            ? ""
            : await peticion.Content.ReadAsStringAsync(cancelacion);

        Peticiones.Add(new Peticion(peticion.Method, peticion.RequestUri!.ToString(), cuerpo));

        return _respuestas.Count > 0
            ? _respuestas.Dequeue()()
            : throw new InvalidOperationException("No quedan respuestas preparadas.");
    }
}

internal sealed record Peticion(HttpMethod Metodo, string Ruta, string Cuerpo);
