# LL.Observabilidad

Nombres estables de trazas y métricas para la cola de eventos, correlación por
`evento.id`, propagación del contexto W3C entre publicar y ack y un registro que
nunca copia la carga.

## Qué aporta

- Un `ActivitySource` y un `Meter` con el nombre `EventosApi`, el mismo que
  escucha el colector de la plataforma.
- Los contadores `eventos.publicados`, `eventos.reclamados`,
  `eventos.aplicados`, `eventos.fallidos` y `eventos.muertos`.
- Actividades para publicar, reclamar, ack y nack, con `evento.id` como
  etiqueta; reclamar añade `evento.tipo` e `intentos`.
- Tramos hijos opcionales `evento.aplicar`, `evento.exportar`,
  `evento.comprobar` y `evento.commitear`, colgados de la entrega en curso.
  Son nombres de conveniencia para el consumidor; el contrato de la cola no
  los exige.
- `SobreDeTraza` y `Propagacion` para llevar el `traceparent` y el
  `tracestate` junto al evento y continuar la traza en el consumidor.
- `RegistroDeEventos`, que solo escribe identificador, tipo, intento y tipo de
  error. No tiene parámetro para la carga y no copia el mensaje de la excepción.

## Uso

```csharp
builder.Services.AddLogging();
builder.Services.AddObservabilidad();
```

El registro de logs es del consumidor: el paquete no lo monta. El exportador
también es del consumidor; configura su colector con la fuente y el medidor
`EventosApi`.

```csharp
public sealed class Publicador
{
    private readonly Telemetria _telemetria;

    public SobreDeTraza? Publicar(string id, string tipo, object carga)
    {
        using var actividad = _telemetria.Publicar(id, tipo);
        _telemetria.Publicados.Add(1);

        // La actividad solo existe si hay un escucha muestreando la fuente.
        return actividad is null ? null : Propagacion.Capturar(actividad, id);
    }
}
```

El sobre se guarda junto al evento. En el consumidor, la entrega continúa la
traza publicada:

```csharp
using var actividad = _telemetria.Continuar(Nombres.Ack, sobre);

using (var aplicar = _telemetria.Aplicar(sobre.EventoId))
{
    // Aplica el cambio. El tramo es opcional: el contrato de la cola no lo exige.
}

_telemetria.Aplicados.Add(1);
Telemetria.EtiquetarEstado(actividad, "aceptado");
```

Las transiciones se registran con el identificador y sin la carga:

```csharp
using var alcance = _registro.Alcance(evento.Id);
_registro.Reclamado(evento.Id, evento.Tipo, evento.Intentos);
_registro.Aplicado(evento.Id, evento.Tipo);
```

## Nombres

| Tipo | Nombre |
|---|---|
| Fuente y medidor | `EventosApi` |
| Actividades | `eventos.publicar`, `eventos.reclamar`, `eventos.ack`, `eventos.nack` |
| Tramos hijos (opcionales) | `evento.aplicar`, `evento.exportar`, `evento.comprobar`, `evento.commitear` |
| Etiquetas | `evento.id`, `evento.tipo`, `evento.estado`, `intentos` |
| Contadores | `eventos.publicados`, `eventos.reclamados`, `eventos.aplicados`, `eventos.fallidos`, `eventos.muertos` |

El contrato de la cola fija la fuente `EventosApi`, la etiqueta `evento.id` y
los cinco contadores. Las etiquetas `evento.tipo`, `evento.estado` e `intentos`
son apoyo de las actividades; el contrato solo exige `evento.id`. Las
actividades de operación reproducen las que ya usa el servicio. Los tramos
hijos no forman parte de ese contrato: son una convención del paquete para que
las fases del consumidor se vean en la misma traza, y un consumidor que no los
use no incumple nada.

El nombre de la fuente se puede cambiar con
`AddObservabilidad(opciones => opciones.NombreDeLaFuente = "OtraFuente")`. Es
una decisión del consumidor: el colector tiene que escuchar ese nombre. Los
contadores conservan sus nombres porque son el contrato.

## Límites

- No exporta trazas ni métricas: declara los instrumentos y el consumidor
  conecta el exportador y el colector.
- Solo propaga el contexto W3C. Una actividad jerárquica o un `traceparent`
  inválido se rechazan en vez de perder la correlación en silencio.
- Sin un escucha que muestree la fuente, `ActivitySource.StartActivity`
  devuelve `null` y no hay traza que capturar.
- El registro no admite la carga ni el mensaje de una excepción. Un motivo que
  necesite texto libre se decide en el consumidor y se revisa aparte.
- La correlación entre procesos exige que la cola guarde el `SobreDeTraza` y lo
  devuelva en el reclamo. Mientras el contrato no transporte ese sobre, la
  propagación está probada en proceso, no de extremo a extremo.

## Estado

| Parte | Estado | Evidencia |
|---|---|---|
| Nombres, etiquetas y actividades | Validado | pruebas de `TelemetriaTest` |
| Tramos hijos opcionales | Validado | prueba de `TelemetriaTest`; son convención del consumidor |
| Contadores | Validado | prueba con `MeterListener` |
| Redacción de la carga | Validado | prueba con logger capturador: el marcador no aparece y el identificador sí |
| Propagación publicar → ack | Parcial | misma traza en proceso; falta que el contrato de la cola transporte el sobre |
| Exportador y colector | Diseño | lo monta el consumidor; el paquete no lo incluye |
