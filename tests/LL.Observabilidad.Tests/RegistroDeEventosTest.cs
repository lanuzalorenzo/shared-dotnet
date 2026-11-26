using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Observabilidad.Tests;

public sealed class RegistroDeEventosTest
{
    private const string Id = "evt_01J9Z6Q3Y8W4T6P2M0K5N7R1CD";
    private const string Marcador = "NO-DEBE-SALIR-EN-UN-LOG-7f3a";

    [Fact]
    public void Never_writes_the_payload()
    {
        var capturador = new RegistroCapturador();
        var registro = new RegistroDeEventos(capturador);

        registro.Publicado(Id, "pedido.creado");
        registro.Fallido(Id, new JsonException($"carga: {Marcador}"), "pedido.creado");

        Assert.DoesNotContain(Marcador, capturador.Texto);
        AssertSinExcepcion(capturador);
    }

    [Fact]
    public void Writes_the_identifier_and_the_error_type()
    {
        var capturador = new RegistroCapturador();
        var registro = new RegistroDeEventos(capturador);

        registro.Publicado(Id, "pedido.creado");
        registro.Fallido(Id, new JsonException($"carga: {Marcador}"), "pedido.creado");

        Assert.Contains(Id, capturador.Texto);
        Assert.Contains("JsonException", capturador.Texto);
        AssertSinExcepcion(capturador);
    }

    [Fact]
    public void The_scope_carries_the_identifier()
    {
        var capturador = new RegistroCapturador();
        var registro = new RegistroDeEventos(capturador);

        using (registro.Alcance(Id))
        {
            registro.Aplicado(Id, "pedido.creado");
        }

        var alcance = Assert.IsType<Dictionary<string, object?>>(Assert.Single(capturador.Estados));
        var entrada = Assert.Single(alcance);

        Assert.Equal("evento.id", entrada.Key);
        Assert.Equal(Id, entrada.Value);
        Assert.Null(Assert.Single(capturador.Excepciones));
    }

    [Fact]
    public void Rejects_an_empty_identifier()
    {
        var registro = new RegistroDeEventos(new RegistroCapturador());

        Assert.Throws<ArgumentException>(() => registro.Publicado(" "));
    }

    [Fact]
    public void Registers_a_single_telemetry_with_the_configured_name()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddObservabilidad(opciones => opciones.NombreDeLaFuente = "MiFuente");

        using var proveedor = servicios.BuildServiceProvider();
        var telemetria = proveedor.GetRequiredService<Telemetria>();

        Assert.Equal("MiFuente", telemetria.Nombre);
        Assert.Same(telemetria, proveedor.GetRequiredService<Telemetria>());
        Assert.NotNull(proveedor.GetRequiredService<RegistroDeEventos>());
    }

    [Fact]
    public void Rejects_an_empty_source_name()
    {
        var servicios = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
        {
            servicios.AddObservabilidad(opciones => opciones.NombreDeLaFuente = " ");
        });
    }

    private static void AssertSinExcepcion(RegistroCapturador capturador)
    {
        Assert.Equal(2, capturador.Excepciones.Count);
        Assert.Null(capturador.Excepciones[0]);
        Assert.Null(capturador.Excepciones[1]);
    }
}
