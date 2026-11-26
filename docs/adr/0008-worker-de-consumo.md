# 0008 — Worker de consumo de la cola de eventos

- **Estado:** Aceptada
- Fecha: 2026-11-26

## Contexto

`LL.Eventos.Client` ya habla el contrato v1: publicar, reclamar, ack con el hash del commit y nack con el motivo. Cada consumidor del portfolio necesita además el bucle que pide trabajo, aplica y cierra, y hoy lo escribiría a mano: uno reclamaría más de lo que puede aplicar, otro olvidaría devolver el lease al apagarse y un tercero no cerraría con el número de entrega vigente. Es el control que el [ADR 0001](0001-scope-and-extraction-criteria.md) extrae cuando lo pide el segundo consumidor y su valor por defecto es una decisión de seguridad.

El contrato impone dos límites que el bucle no puede ignorar. La entrega es única y con lease: mientras está reclamada, otro consumidor no la recibe; si el lease vence, el siguiente reclamo la reentrega con un intento más y un ack con el testigo viejo responde 409. Y un ack solo es válido con el hash del commit que aplicó el cambio y el número de entrega de ese lease, no el de una entrega anterior.

## Decisión

`shared-dotnet` publica `LL.Eventos.Worker`.

1. **El worker compone el cliente, no lo sustituye.** Recibe un `EventosClient` ya configurado y resuelve un `IEventosHandler` en un alcance por entrega; el transporte, la dirección y la autenticación siguen siendo del consumidor, y el manejador puede depender de servicios con ámbito, como un contexto de datos. El paquete no conoce el dominio: la carga es un `JsonElement` y el manejador decide qué hacer con ella.
2. **El manejador devuelve el hash del commit.** `HandleAsync(Evento, CancellationToken)` termina con el hash de la confirmación; el worker cierra con `AckAsync(id, hash, evento.Intentos)` y nunca inventa el número de entrega. Si el manejador lanza o devuelve un hash vacío, el evento se rechaza con `NackAsync` y el motivo; no se ackea trabajo que no se aplicó.
3. **Un hueco por conversación.** Cada bucle tiene una entrega en curso como máximo y reclama la siguiente cuando cierra la anterior; `ConcurrenciaMaxima` fija cuántos bucles hay. El worker no reclama en paralelo más de lo que puede aplicar, que es lo que produce leases vencidos y reentregas.
4. **Un fallo de transporte no tumba el proceso.** Un error al reclamar se registra una línea y se espera `EsperaTrasError` antes de reintentar; un 409 al cerrar significa que el lease o el testigo cambiaron y también se registra, porque la cola ya asignó ese trabajo a otro. Un reclamo sin identificador no se puede cerrar: se registra y se descarta, y la entrega vuelve cuando vence su lease. El host solo se detiene con la parada.
5. **El apagado deja de reclamar y termina lo empezado.** Al llegar la señal de parada no se lanza ningún reclamo nuevo y un reclamo ya enviado no se cancela: si el servidor concedió el lease y se perdiera la respuesta, la entrega quedaría retenida hasta que venciera. Si ese reclamo vuelve con un evento, se devuelve en el acto. Los manejadores reciben un plazo de `TiempoDeApagado` para cerrar; si vence, se cancelan y el worker devuelve el lease con un nack `apagado`, para que otro consumidor lo recoja sin esperar los 300 s del contrato. Nunca deja la entrega reclamada ni ackea un trabajo interrumpido. El consumidor fija ese plazo por debajo del lease.
6. **La sonda mide atascos, no atrasos.** `SondaDeCola` sigue las entregas en curso, con la clave `(identificador, intento)` porque el mismo evento puede reentregarse con otro lease, y expone si alguna superó el `LeaseHasta` del contrato —la cola ya puede reentregarla— o, cuando el servidor no manda lease, el `UmbralDeAtasco`. Una cola vacía no es un atasco y no se convierte en fallo de salud: el atraso no es una avería. La sonda se consulta; no cambia el estado de la cola.
7. **El registro no lleva la carga.** Las líneas del worker nombran el identificador del evento, el intento y el motivo, nunca la carga. El motivo de un nack es el del manejador, truncado, y su redacción es responsabilidad del consumidor.
8. **Se prueba contra un doble del contrato con lease.** Las pruebas de `LL.Eventos.Worker` usan un `HttpMessageHandler` que implementa la cola —estados, lease, testigo de entrega y ack/nack— y hablan el JSON `snake_case` del contrato. Dos consumidores sobre ese doble entregan cada evento una vez por lease; el apagado y la sonda se prueban con reloj y tiempos cortos.

## Alternativas descartadas

- **Un bucle propio en cada consumidor.** Repite la concurrencia, el apagado y el testigo de entrega, y basta que uno ackee con el intento equivocado para aplicar dos veces el mismo cierre.
- **Reclamar en paralelo y cerrar después.** Aumenta el rendimiento y multiplica los leases vencidos: con un manejador lento, la cola reentrega mientras el primer consumidor todavía trabaja.
- **Confiar en el lease y no devolver trabajo al apagar.** El evento queda retenido hasta que vence el lease —300 s— sin que nadie lo aplique. El nack de apagado lo devuelve en el acto y conserva el motivo.
- **Ackear lo empezado al apagar.** Convierte un trabajo interrumpido en un cierre falso y pierde el evento; es el fallo que la prueba de apagado impide.
- **Una interfaz de cola propia dentro del worker.** Duplicaría `EventosClient` y escondería el contrato real en las pruebas. El doble habla HTTP y JSON del contrato, no una abstracción intermedia.
- **Sonda de salud que falla con la cola vacía o con atraso.** Convierte una cola sin trabajo en una avería y obliga a apagar el servicio por falta de eventos.

## Consecuencias

- El consumidor registra un `IEventosHandler`, configura las opciones y arranca; no escribe el bucle.
- Un manejador idempotente sigue siendo su responsabilidad: el worker cierra una vez por lease, pero un lease vencido puede reentregar y el consumidor debe tolerar repetir.
- El nack de apagado consume un intento del contrato y arranca la espera de reintento; con cinco nacks el evento queda muerto. Es el precio de devolver el lease en el acto; quien prefiera no gastar intentos puede alargar `TiempoDeApagado` hasta que el manejador termine.
- Las fechas y el lease del evento llegan del contrato `snake_case`; el cliente del [ADR 0007](0007-cliente-de-eventos.md) se corrige en el mismo cambio para leerlos con los nombres de la API.
- La publicación del paquete usa el tag `worker/vX.Y.Z`, un tag por paquete como fija el [ADR 0002](0002-versioning-and-feed.md). El worker arrastra al cliente: se publica primero `eventos/v0.1.1` —el cliente que lee `snake_case`— y después `worker/v0.1.1`, con la misma versión, porque el `-p:Version` del tag fija también la dependencia que el paquete declara.
