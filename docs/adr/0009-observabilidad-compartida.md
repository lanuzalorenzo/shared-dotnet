# 0009 — Observabilidad compartida de la cola

- **Estado:** Aceptada
- Fecha: 2026-11-26

## Contexto

La cola de eventos ya declara su instrumentación con una fuente `EventosApi`, cinco contadores `eventos.*` y una actividad por operación con `evento.id`. Está escrita dentro del servicio, así que el siguiente consumidor repetiría los nombres o inventaría otros; un colector que escuche una fuente no vería la otra. Además, la carga del evento es opaca y puede contener datos del cierre: una línea de registro que la copie convierte el log en una segunda copia del estado.

El paquete no puede conocer el dominio ni el contrato de la cola más allá de su sobre mínimo. La correlación tiene que viajar con el evento, no dentro del paquete.

## Decisión

`shared-dotnet` publica `LL.Observabilidad`.

1. **Una sola fuente por proceso.** `Telemetria` declara el `ActivitySource` y el `Meter` con el mismo nombre y crea los cinco contadores. El nombre por defecto es `EventosApi`, el del contrato; una aplicación que necesite otra fuente lo cambia por opciones y configura su colector con ese nombre. No hay una segunda tabla de nombres.
2. **Actividades con el identificador del evento.** `Publicar`, `Reclamar`, `Ack` y `Nack` abren la actividad de la operación y ponen `evento.id`; reclamar añade `evento.tipo` e `intentos`. El contrato de la cola solo fija la fuente `EventosApi`, la etiqueta `evento.id` y los cinco contadores; las actividades de operación reproducen las del servicio. `Aplicar`, `Exportar`, `Comprobar` y `Commitear` son tramos hijos opcionales de conveniencia para el consumidor, no obligaciones del paquete ni nombres del contrato. Los nombres y las etiquetas son constantes públicas en `Nombres`.
3. **Contadores de transición.** `eventos.publicados`, `eventos.reclamados`, `eventos.aplicados`, `eventos.fallidos` y `eventos.muertos` se exponen como `Counter<long>`; el consumidor los incrementa cuando la operación se confirma.
4. **Propagación W3C explícita.** `Propagacion.Capturar` toma el `traceparent` y el `tracestate` de la actividad que publica y los devuelve en un `SobreDeTraza` con el identificador del evento; `Continuar` abre la actividad del consumidor como hija de ese contexto, con el mismo tipo de cierre que ack y nack. Solo se admite W3C: un contexto inválido se rechaza en vez de perder la traza en silencio.
5. **Registro sin carga.** `RegistroDeEventos` solo acepta identificador, tipo, intento y tipo de error. No hay parámetro para la carga y el mensaje de una excepción no se escribe, porque puede contenerla. El alcance de log lleva `evento.id`, no el cuerpo.
6. **Sin exportador.** El paquete declara los instrumentos; el exportador, el colector y los paneles son del consumidor. Las pruebas usan `ActivityListener` y `MeterListener` en proceso, igual que el contrato que envuelven.

## Alternativas descartadas

- **Dejar los nombres en cada consumidor.** Es lo que hay: dos servicios que publican el mismo evento con fuentes distintas no se pueden correlacionar en un panel.
- **Adoptar el SDK de OpenTelemetry dentro del paquete.** Añade configuración, exportador y una versión que el consumidor no controla; los instrumentos de la BCL son la base y el consumidor decide a dónde van.
- **Propagar dentro del cliente de eventos.** Ataría el sobre de traza al contrato HTTP y obligaría a una versión incompatible de `LL.Eventos.Client`; el sobre se guarda junto al evento y el cliente no cambia.
- **Registrar la carga para depurar.** Convierte el log en una copia del dato y puede filtrar el contenido del cierre; la traza por identificador basta.

## Consecuencias

- La fuente `EventosApi` y los cinco contadores `eventos.*` quedan fijados en un paquete; un cambio de nombre es una ruptura para el colector. Los tramos `evento.*` son una convención opcional del consumidor y no forman parte de ese contrato.
- La correlación entre procesos exige que la cola persista el `SobreDeTraza` y lo devuelva en el reclamo. Mientras el contrato v1 no transporte ese sobre, la propagación está probada en proceso y el estado del control es Parcial, no Validado de extremo a extremo.
- El registro no puede volcar la carga por accidente; un motivo con texto libre sigue siendo responsabilidad del consumidor.
- La publicación usa el tag `observabilidad/vX.Y.Z`, un tag por paquete como fija el [ADR 0002](0002-versioning-and-feed.md).
