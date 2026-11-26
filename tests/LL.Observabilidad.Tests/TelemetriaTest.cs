using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LL.Observabilidad.Tests;

public sealed class TelemetriaTest
{
    [Fact]
    public void Exposes_the_contract_names()
    {
        using var telemetria = new Telemetria();

        Assert.Equal("EventosApi", Nombres.Fuente);
        Assert.Equal("EventosApi", telemetria.Nombre);
        Assert.Equal("EventosApi", telemetria.Actividades.Name);
        Assert.Equal("EventosApi", telemetria.Medidor.Name);
        Assert.Equal("eventos.publicados", telemetria.Publicados.Name);
        Assert.Equal("eventos.reclamados", telemetria.Reclamados.Name);
        Assert.Equal("eventos.aplicados", telemetria.Aplicados.Name);
        Assert.Equal("eventos.fallidos", telemetria.Fallidos.Name);
        Assert.Equal("eventos.muertos", telemetria.Muertos.Name);

        Assert.Equal("evento.id", Nombres.EtiquetaEvento);
        Assert.Equal("evento.tipo", Nombres.EtiquetaTipo);
        Assert.Equal("evento.estado", Nombres.EtiquetaEstado);
        Assert.Equal("intentos", Nombres.EtiquetaIntentos);
        Assert.Equal("eventos.publicar", Nombres.Publicar);
        Assert.Equal("eventos.reclamar", Nombres.Reclamar);
        Assert.Equal("eventos.ack", Nombres.Ack);
        Assert.Equal("eventos.nack", Nombres.Nack);
        Assert.Equal("evento.aplicar", Nombres.Aplicar);
        Assert.Equal("evento.exportar", Nombres.Exportar);
        Assert.Equal("evento.comprobar", Nombres.Comprobar);
        Assert.Equal("evento.commitear", Nombres.Commitear);
        Assert.Equal(ActivityKind.Consumer, Telemetria.TipoDeCierre);
    }

    [Fact]
    public void Starts_a_publish_activity_with_the_event_tags()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();
        using var actividad = telemetria.Publicar("evt_01", "pedido.creado");

        Assert.NotNull(actividad);
        Assert.Equal("eventos.publicar", actividad.OperationName);
        Assert.Equal(ActivityKind.Producer, actividad.Kind);
        Assert.Equal("evt_01", actividad.GetTagItem("evento.id"));
        Assert.Equal("pedido.creado", actividad.GetTagItem("evento.tipo"));
    }

    [Fact]
    public void Adds_the_delivery_tags_to_the_claim()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();
        using var actividad = telemetria.Reclamar("evt_01", "pedido.creado", 2);

        Assert.NotNull(actividad);
        Assert.Equal("eventos.reclamar", actividad.OperationName);
        Assert.Equal(ActivityKind.Consumer, actividad.Kind);
        Assert.Equal("evt_01", actividad.GetTagItem("evento.id"));
        Assert.Equal("pedido.creado", actividad.GetTagItem("evento.tipo"));
        Assert.Equal(2, actividad.GetTagItem("intentos"));
    }

    [Fact]
    public void Starts_ack_and_nack_with_the_same_kind()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();

        using (var ack = telemetria.Ack("evt_01", 1))
        {
            Assert.NotNull(ack);
            Assert.Equal("eventos.ack", ack.OperationName);
            Assert.Equal(Telemetria.TipoDeCierre, ack.Kind);
            Assert.Equal("evt_01", ack.GetTagItem("evento.id"));
            Assert.Equal(1, ack.GetTagItem("intentos"));
        }

        using (var nack = telemetria.Nack("evt_01", 2))
        {
            Assert.NotNull(nack);
            Assert.Equal("eventos.nack", nack.OperationName);
            Assert.Equal(Telemetria.TipoDeCierre, nack.Kind);
            Assert.Equal("evt_01", nack.GetTagItem("evento.id"));
            Assert.Equal(2, nack.GetTagItem("intentos"));
        }
    }

    [Fact]
    public void Child_steps_hang_from_the_delivery()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();
        using var entrega = telemetria.Reclamar("evt_01", "pedido.creado", 1);
        Assert.NotNull(entrega);

        using (var aplicar = telemetria.Aplicar("evt_01"))
        {
            AssertTramo(entrega, aplicar, "evento.aplicar");
        }

        using (var exportar = telemetria.Exportar("evt_01"))
        {
            AssertTramo(entrega, exportar, "evento.exportar");
        }

        using (var comprobar = telemetria.Comprobar("evt_01"))
        {
            AssertTramo(entrega, comprobar, "evento.comprobar");
        }

        using (var commitear = telemetria.Commitear("evt_01"))
        {
            AssertTramo(entrega, commitear, "evento.commitear");
        }
    }

    [Fact]
    public void Counts_the_transitions()
    {
        using var escucha = new EscuchaDeMetricas();
        using var telemetria = new Telemetria();

        telemetria.Publicados.Add(1);
        telemetria.Reclamados.Add(2);
        telemetria.Aplicados.Add(3);
        telemetria.Fallidos.Add(4);
        telemetria.Muertos.Add(5);

        Assert.Equal(1, escucha.Valor("eventos.publicados"));
        Assert.Equal(2, escucha.Valor("eventos.reclamados"));
        Assert.Equal(3, escucha.Valor("eventos.aplicados"));
        Assert.Equal(4, escucha.Valor("eventos.fallidos"));
        Assert.Equal(5, escucha.Valor("eventos.muertos"));
    }

    [Fact]
    public void Uses_the_configured_source_name()
    {
        using var telemetria = new Telemetria("MiFuente");

        Assert.Equal("MiFuente", telemetria.Nombre);
        Assert.Equal("MiFuente", telemetria.Actividades.Name);
        Assert.Equal("MiFuente", telemetria.Medidor.Name);
    }

    [Fact]
    public void Rejects_an_empty_source_name()
    {
        Assert.Throws<ArgumentException>(() => new Telemetria(" "));
    }

    [Fact]
    public void Rejects_an_empty_identifier()
    {
        using var escucha = new EscuchaDeActividades();
        using var telemetria = new Telemetria();

        Assert.Throws<ArgumentException>(() => telemetria.Publicar(" "));
        Assert.Throws<ArgumentException>(() => telemetria.Ack(" "));
    }

    private static void AssertTramo(Activity padre, Activity? tramo, string nombre)
    {
        Assert.NotNull(tramo);
        Assert.Equal(nombre, tramo.OperationName);
        Assert.Equal(ActivityKind.Internal, tramo.Kind);
        Assert.Equal(padre.SpanId, tramo.ParentSpanId);
        Assert.Equal("evt_01", tramo.GetTagItem("evento.id"));
    }
}
