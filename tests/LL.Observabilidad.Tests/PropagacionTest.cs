namespace LL.Observabilidad.Tests;

public sealed class PropagacionTest
{
    [Fact]
    public void The_trace_travels_from_publish_to_ack()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();

        var publicacion = telemetria.Publicar("evt_01", "pedido.creado");
        Assert.NotNull(publicacion);

        var traza = publicacion.TraceId;
        var tramo = publicacion.SpanId;
        SobreDeTraza sobre;

        using (publicacion)
        {
            sobre = Propagacion.Capturar(publicacion, "evt_01");
        }

        Assert.Contains(traza.ToHexString(), sobre.Traceparent, StringComparison.Ordinal);

        using var acuse = telemetria.Continuar(Nombres.Ack, sobre);

        Assert.NotNull(acuse);
        Assert.Equal(traza, acuse.TraceId);
        Assert.Equal(tramo, acuse.ParentSpanId);
        Assert.Equal(Telemetria.TipoDeCierre, acuse.Kind);
        Assert.Equal("evt_01", acuse.GetTagItem("evento.id"));
    }

    [Fact]
    public void Rejects_a_traceparent_outside_w3c()
    {
        using var telemetria = new Telemetria();
        var sobre = new SobreDeTraza("no-es-w3c", null, "evt_01");

        Assert.Throws<ArgumentException>(() =>
        {
            telemetria.Continuar(Nombres.Ack, sobre);
        });
    }

    [Fact]
    public void Rejects_an_empty_identifier_when_capturing()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();
        using var publicacion = telemetria.Publicar("evt_01", "pedido.creado");
        Assert.NotNull(publicacion);

        Assert.Throws<ArgumentException>(() => Propagacion.Capturar(publicacion, " "));
    }
}
