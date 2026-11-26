using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LL.Eventos.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LL.Eventos.Worker.Tests;

public sealed class EventosWorkerTest
{
    [Fact]
    public async Task Delivers_and_acks_each_event_once()
    {
        var cola = new ColaDoble();
        var ids = new[] { cola.Publicar("pedido.creado"), cola.Publicar("pedido.creado"), cola.Publicar("pedido.creado") };
        var vista = new ConcurrentBag<Evento>();
        var (worker, _) = Crear(cola, new Manejador((evento, _) =>
        {
            vista.Add(evento);
            return Task.FromResult("hash-commit");
        }), Opciones(concurrencia: 2));

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Acuses.Count == ids.Length);
        await worker.StopAsync(default);

        Assert.Equal(ids.Length, cola.Acuses.Select(acuse => acuse.Id).Distinct().Count());
        Assert.Empty(cola.Rechazos);
        Assert.All(ids, id => Assert.Equal("aceptado", cola.EstadoDe(id)!.Estado));

        var primero = Assert.Single(vista, evento => evento.Id == ids[0]);
        Assert.NotEqual(default, primero.PublicadoEn);
        Assert.NotNull(primero.LeaseHasta);
    }

    [Fact]
    public async Task Two_consumers_deliver_each_event_once_per_lease()
    {
        var cola = new ColaDoble();
        var ids = Enumerable.Range(0, 20).Select(_ => cola.Publicar("pedido.creado")).ToArray();
        var aplicados = new ConcurrentBag<string>();

        Task<string> Aplicar(Evento evento, CancellationToken _)
        {
            aplicados.Add(evento.Id);
            return Task.FromResult("hash-commit");
        }

        var (primero, _) = Crear(cola, new Manejador(Aplicar), Opciones(concurrencia: 2));
        var (segundo, _) = Crear(cola, new Manejador(Aplicar), Opciones(concurrencia: 2));

        await primero.StartAsync(default);
        await segundo.StartAsync(default);
        await EsperarHastaAsync(() => cola.Acuses.Count == ids.Length, TimeSpan.FromSeconds(15));
        await primero.StopAsync(default);
        await segundo.StopAsync(default);

        Assert.Equal(ids.Length, aplicados.Distinct().Count());
        Assert.Empty(cola.Rechazos);
        Assert.All(ids, id =>
        {
            var evento = cola.EstadoDe(id)!;
            Assert.Equal("aceptado", evento.Estado);
            Assert.Equal(1, evento.Intentos);
        });
    }

    [Fact]
    public async Task Never_runs_more_handlers_than_the_concurrency()
    {
        var cola = new ColaDoble();
        for (var i = 0; i < 6; i++)
        {
            cola.Publicar("pedido.creado");
        }

        var suelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activos = 0;
        var maximo = 0;
        var entrados = 0;

        async Task<string> Aplicar(Evento _, CancellationToken cancelacion)
        {
            var enCurso = Interlocked.Increment(ref activos);
            Interlocked.Increment(ref entrados);
            Maximo(ref maximo, enCurso);

            try
            {
                await suelta.Task.WaitAsync(cancelacion);
                return "hash-commit";
            }
            finally
            {
                Interlocked.Decrement(ref activos);
            }
        }

        var (worker, _) = Crear(cola, new Manejador(Aplicar), Opciones(concurrencia: 2));

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => Volatile.Read(ref entrados) == 2);
        await Task.Delay(200);
        Assert.Equal(2, Volatile.Read(ref maximo));
        Assert.Equal(2, Volatile.Read(ref entrados));

        suelta.SetResult();
        await EsperarHastaAsync(() => cola.Acuses.Count == 6);
        await worker.StopAsync(default);

        Assert.Equal(2, Volatile.Read(ref maximo));
    }

    [Fact]
    public async Task Resolves_the_handler_in_a_scope_per_delivery()
    {
        var cola = new ColaDoble();
        cola.Publicar("pedido.creado");
        cola.Publicar("pedido.creado");

        var instancias = new ConcurrentBag<int>();
        var contador = 0;
        var servicios = new ServiceCollection();
        servicios.AddTransient<IEventosHandler>(_ => new Manejador((_, _) =>
        {
            instancias.Add(Interlocked.Increment(ref contador));
            return Task.FromResult("hash-commit");
        }));
        using var proveedor = servicios.BuildServiceProvider();

        var (worker, _) = Crear(
            cola,
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            Opciones());

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Acuses.Count == 2);
        await worker.StopAsync(default);

        Assert.Equal(2, instancias.Distinct().Count());
    }

    [Fact]
    public async Task Skips_a_claim_without_identifier()
    {
        var cola = new ColaDoble();
        cola.Publicar("pedido.creado");
        cola.IdVacioEnElProximoReclamo = true;
        var valido = cola.Publicar("pedido.creado");
        var registro = new Registro();

        var (worker, _) = Crear(
            cola,
            new Manejador((_, _) => Task.FromResult("hash-commit")),
            Opciones(),
            registro);

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Acuses.Count == 1);
        await worker.StopAsync(default);

        Assert.Equal(valido, Assert.Single(cola.Acuses).Id);
        Assert.Contains("identificador", string.Join('\n', registro.Lineas));
    }

    [Fact]
    public async Task Rejects_with_the_motive_when_the_handler_fails()
    {
        var cola = new ColaDoble { EsperaDeReintento = TimeSpan.FromHours(1) };
        var id = cola.Publicar("pedido.creado");
        var (worker, _) = Crear(
            cola,
            new Manejador((_, _) => throw new InvalidOperationException("sin base de datos")),
            Opciones());

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Rechazos.Count == 1);
        await worker.StopAsync(default);

        Assert.Empty(cola.Acuses);
        var rechazo = Assert.Single(cola.Rechazos);
        Assert.Equal(id, rechazo.Id);
        Assert.Contains("sin base de datos", rechazo.Motivo);
        Assert.Equal("fallido", cola.EstadoDe(id)!.Estado);
    }

    [Fact]
    public async Task Rejects_an_empty_hash_instead_of_closing()
    {
        var cola = new ColaDoble { EsperaDeReintento = TimeSpan.FromHours(1) };
        var id = cola.Publicar("pedido.creado");
        var (worker, _) = Crear(cola, new Manejador((_, _) => Task.FromResult(" ")), Opciones());

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Rechazos.Count == 1);
        await worker.StopAsync(default);

        Assert.Empty(cola.Acuses);
        Assert.Contains("hash", Assert.Single(cola.Rechazos).Motivo);
        Assert.Equal("fallido", cola.EstadoDe(id)!.Estado);
    }

    [Fact]
    public async Task Finishes_the_delivery_in_flight_on_shutdown()
    {
        var cola = new ColaDoble();
        var id = cola.Publicar("pedido.creado");
        var suelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (worker, _) = Crear(cola, new Manejador(async (_, cancelacion) =>
        {
            entrado.TrySetResult();
            await suelta.Task.WaitAsync(cancelacion);
            return "hash-commit";
        }), Opciones());

        await worker.StartAsync(default);
        await entrado.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var parada = worker.StopAsync(default);
        await Task.Delay(50);
        suelta.SetResult();
        await parada.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("aceptado", cola.EstadoDe(id)!.Estado);
        Assert.Empty(cola.Rechazos);
    }

    [Fact]
    public async Task Returns_a_delivery_granted_while_stopping()
    {
        var cola = new ColaDoble();
        var id = cola.Publicar("pedido.creado");
        var puerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cola.EsperaDeReclamo = puerta.Task;

        var aplicados = 0;
        var (worker, _) = Crear(cola, new Manejador((_, _) =>
        {
            Interlocked.Increment(ref aplicados);
            return Task.FromResult("hash-commit");
        }), Opciones());

        await worker.StartAsync(default);
        await Task.Delay(50);

        var parada = worker.StopAsync(default);
        puerta.SetResult();
        await parada.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, Volatile.Read(ref aplicados));
        Assert.Equal("apagado", Assert.Single(cola.Rechazos).Motivo);
        Assert.Equal("fallido", cola.EstadoDe(id)!.Estado);
    }

    [Fact]
    public async Task Returns_the_lease_on_shutdown_when_the_handler_does_not_finish()
    {
        var cola = new ColaDoble();
        var id = cola.Publicar("pedido.creado");
        var entrado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nunca = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var opciones = Opciones();
        opciones.TiempoDeApagado = TimeSpan.FromMilliseconds(150);
        var (worker, _) = Crear(cola, new Manejador(async (_, _) =>
        {
            entrado.TrySetResult();
            await nunca.Task;
            return "hash-commit";
        }), opciones);

        await worker.StartAsync(default);
        await entrado.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        var rechazo = Assert.Single(cola.Rechazos);
        Assert.Equal(id, rechazo.Id);
        Assert.Equal("apagado", rechazo.Motivo);
        Assert.Equal("fallido", cola.EstadoDe(id)!.Estado);
    }

    [Fact]
    public async Task Does_not_log_the_handler_message()
    {
        var cola = new ColaDoble { EsperaDeReintento = TimeSpan.FromHours(1) };
        cola.Publicar("pedido.creado");
        var registro = new Registro();

        var (worker, _) = Crear(
            cola,
            new Manejador((_, _) => throw new JsonException("carga secreta 4242")),
            Opciones(),
            registro);

        await worker.StartAsync(default);
        await EsperarHastaAsync(() => cola.Rechazos.Count == 1);
        await worker.StopAsync(default);

        Assert.Contains("JsonException", string.Join('\n', registro.Lineas));
        Assert.DoesNotContain("4242", string.Join('\n', registro.Lineas));
    }

    [Fact]
    public async Task Probe_flags_a_delivery_past_its_lease()
    {
        var cola = new ColaDoble { Lease = TimeSpan.FromMilliseconds(300) };
        var id = cola.Publicar("pedido.creado");
        var entrado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nunca = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (worker, sonda) = Crear(cola, new Manejador(async (_, _) =>
        {
            entrado.TrySetResult();
            await nunca.Task;
            return "hash-commit";
        }), Opciones());

        await worker.StartAsync(default);
        await entrado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(400);

        var estado = sonda.Estado;
        Assert.True(estado.Atascada);
        Assert.Equal(id, estado.EventoAtascado);
        Assert.NotNull(estado.MasAntigua);

        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Probe_does_not_flag_an_empty_queue()
    {
        var cola = new ColaDoble();
        var (worker, sonda) = Crear(cola, new Manejador((_, _) => Task.FromResult("hash")), Opciones());

        await worker.StartAsync(default);
        await Task.Delay(300);

        Assert.False(sonda.Estado.Atascada);
        Assert.Equal(0, sonda.Estado.EnCurso);
        Assert.True(cola.ReclamosVacios >= 2);

        await worker.StopAsync(default);
    }

    private static EventosWorkerOptions Opciones(int concurrencia = 1)
    {
        return new EventosWorkerOptions
        {
            ConcurrenciaMaxima = concurrencia,
            EsperaSinTrabajo = TimeSpan.FromMilliseconds(10),
            EsperaTrasError = TimeSpan.FromMilliseconds(10),
            TiempoDeApagado = TimeSpan.FromSeconds(2),
        };
    }

    private static (EventosWorker Worker, SondaDeCola Sonda) Crear(
        ColaDoble cola,
        IEventosHandler manejador,
        EventosWorkerOptions opciones,
        ILogger<EventosWorker>? registro = null)
    {
        var servicios = new ServiceCollection();
        servicios.AddScoped(_ => manejador);
        var proveedor = servicios.BuildServiceProvider();

        return Crear(cola, proveedor.GetRequiredService<IServiceScopeFactory>(), opciones, registro);
    }

    private static (EventosWorker Worker, SondaDeCola Sonda) Crear(
        ColaDoble cola,
        IServiceScopeFactory alcances,
        EventosWorkerOptions opciones,
        ILogger<EventosWorker>? registro = null)
    {
        var http = new HttpClient(cola) { BaseAddress = new Uri("https://eventos.test/") };
        var cliente = new EventosClient(
            http,
            new EventosClientOptions { Reintentos = 0, EsperaBase = TimeSpan.Zero });
        var sonda = new SondaDeCola(opciones.UmbralDeAtasco);

        return (new EventosWorker(cliente, alcances, opciones, sonda, registro), sonda);
    }

    private static void Maximo(ref int maximo, int valor)
    {
        int actual;

        while ((actual = Volatile.Read(ref maximo)) < valor
            && Interlocked.CompareExchange(ref maximo, valor, actual) != actual)
        {
        }
    }

    private static async Task EsperarHastaAsync(Func<bool> condicion, TimeSpan? plazo = null)
    {
        var limite = DateTimeOffset.UtcNow + (plazo ?? TimeSpan.FromSeconds(10));

        while (!condicion())
        {
            if (DateTimeOffset.UtcNow > limite)
            {
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class Manejador(Func<Evento, CancellationToken, Task<string>> aplicar) : IEventosHandler
    {
        public Task<string> HandleAsync(Evento evento, CancellationToken cancelacion)
        {
            return aplicar(evento, cancelacion);
        }
    }

    private sealed class Registro : ILogger<EventosWorker>
    {
        public ConcurrentQueue<string> Lineas { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lineas.Enqueue(formatter(state, exception));
        }
    }
}

public sealed class SondaDeColaTest
{
    [Fact]
    public void Uses_the_lease_when_the_contract_sends_it()
    {
        var reloj = new RelojFalso();
        var sonda = new SondaDeCola(TimeSpan.FromHours(1), reloj);
        using var entrega = sonda.EnCurso("evt_01", 1, reloj.GetUtcNow() + TimeSpan.FromMinutes(1));

        reloj.Ahora = reloj.Ahora.AddSeconds(30);
        Assert.False(sonda.Estado.Atascada);

        reloj.Ahora = reloj.Ahora.AddSeconds(31);
        Assert.True(sonda.Estado.Atascada);
        Assert.Equal("evt_01", sonda.Estado.EventoAtascado);
    }

    [Fact]
    public void Uses_the_threshold_when_there_is_no_lease()
    {
        var reloj = new RelojFalso();
        var sonda = new SondaDeCola(TimeSpan.FromMinutes(1), reloj);
        using var entrega = sonda.EnCurso("evt_01", 1, null);

        reloj.Ahora = reloj.Ahora.AddSeconds(59);
        Assert.False(sonda.Estado.Atascada);

        reloj.Ahora = reloj.Ahora.AddSeconds(2);
        Assert.True(sonda.Estado.Atascada);
    }

    [Fact]
    public void Counts_two_deliveries_of_the_same_event()
    {
        var sonda = new SondaDeCola(TimeSpan.FromMinutes(5));
        var primera = sonda.EnCurso("evt_01", 1, null);
        var segunda = sonda.EnCurso("evt_01", 2, null);

        Assert.Equal(2, sonda.Estado.EnCurso);

        primera.Dispose();
        Assert.Equal(1, sonda.Estado.EnCurso);

        segunda.Dispose();
        Assert.Equal(0, sonda.Estado.EnCurso);
    }

    [Fact]
    public void Keeps_the_newest_entry_when_the_same_delivery_is_opened_twice()
    {
        var sonda = new SondaDeCola(TimeSpan.FromMinutes(5));
        var primera = sonda.EnCurso("evt_01", 1, null);
        var segunda = sonda.EnCurso("evt_01", 1, null);

        primera.Dispose();
        Assert.Equal(1, sonda.Estado.EnCurso);

        segunda.Dispose();
        Assert.Equal(0, sonda.Estado.EnCurso);
    }

    private sealed class RelojFalso : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = DateTimeOffset.Parse(
            "2026-11-26T08:00:00+00:00",
            CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => Ahora;
    }
}
