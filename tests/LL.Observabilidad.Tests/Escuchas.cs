using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace LL.Observabilidad.Tests;

/// <summary>Escucha local de actividades, sin exportador.</summary>
internal sealed class EscuchaDeActividades : IDisposable
{
    private readonly ActivityListener _escucha;

    public EscuchaDeActividades()
    {
        _escucha = new ActivityListener
        {
            ShouldListenTo = fuente => fuente.Name == Nombres.Fuente,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => { },
        };

        ActivitySource.AddActivityListener(_escucha);
    }

    public void Dispose()
    {
        _escucha.Dispose();
    }
}

/// <summary>Escucha local de los contadores del medidor.</summary>
internal sealed class EscuchaDeMetricas : IDisposable
{
    private readonly ConcurrentDictionary<string, long> _valores = new();
    private readonly MeterListener _escucha;

    public EscuchaDeMetricas()
    {
        _escucha = new MeterListener
        {
            InstrumentPublished = (instrumento, escucha) =>
            {
                if (instrumento.Meter.Name == Nombres.Fuente)
                {
                    escucha.EnableMeasurementEvents(instrumento);
                }
            },
        };

        _escucha.SetMeasurementEventCallback<long>((instrumento, valor, _, _) =>
            _valores.AddOrUpdate(instrumento.Name, valor, (_, actual) => actual + valor));
        _escucha.Start();
    }

    public long Valor(string nombre)
    {
        return _valores.GetValueOrDefault(nombre);
    }

    public void Dispose()
    {
        _escucha.Dispose();
    }
}

/// <summary>Logger que guarda el texto formateado y los estados de alcance.</summary>
internal sealed class RegistroCapturador : ILogger<RegistroDeEventos>
{
    public List<string> Lineas { get; } = [];

    public List<Exception?> Excepciones { get; } = [];

    public List<object?> Estados { get; } = [];

    public string Texto => string.Join('\n', Lineas);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        Estados.Add(state);

        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Lineas.Add(formatter(state, exception));
        Excepciones.Add(exception);
    }
}
