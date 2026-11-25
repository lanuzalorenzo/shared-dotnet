# 0007 — Cliente del contrato de eventos en `shared-dotnet`

- **Estado:** Aceptada
- Fecha: 2026-11-25

## Contexto

El contrato v1 de la cola de eventos ya está cerrado: publicar, reclamar, ack y nack, con un identificador ULID como clave de idempotencia, un lease por entrega y ProblemDetails en los errores. La API vive en su repositorio y los dos consumidores del portfolio —el banco de pruebas y la aplicación principal— van a publicar y consumir eventos.

Si cada consumidor escribe su cliente HTTP, repite el sobre, el formato del identificador, el manejo de errores y la política de reintentos. Y lo hace con matices: uno reenvía la publicación con un identificador nuevo y duplica el evento; otro reintenta el reclamar y pide dos leases. Es exactamente el tipo de pieza que el [ADR 0001](0001-scope-and-extraction-criteria.md) extrae en el segundo consumidor.

El contrato no tiene dominio: un evento es un tipo, una carga JSON opaca y un estado. El cliente puede hablar el contrato sin conocer pedidos, facturas ni ninguna otra entidad del consumidor.

## Decisión

`shared-dotnet` publica `LL.Eventos.Client`.

1. **Solo tipos del contrato.** `Evento`, `Acuse`, `Rechazo` y las operaciones del cliente reflejan la representación v1; la carga es un `JsonElement` y se envía tal como llega. El paquete no interpreta ni valida la carga y no conoce el dominio de ningún consumidor.
2. **El identificador es la clave de idempotencia.** Sin identificador, el cliente genera un ULID con el prefijo `evt_` y la misma forma que valida la API; con identificador, se usa el que pasa el consumidor. Reintentar una publicación conserva ese identificador, y la API devuelve el evento existente en vez de crear otro.
3. **Solo se reintenta la publicación.** Es la única operación idempotente por diseño: repetirla con el mismo identificador no cambia el resultado. Reclamar cambia el lease, y ack y nack cambian el estado; un reintento ciego tras una respuesta perdida puede duplicar la entrega o cerrar dos veces. Esos tres no se reintentan solos.
4. **El reintento es del transporte, no del negocio.** Se repite ante una excepción de envío, un 408, un 429 o un 5xx, con retroceso exponencial y un número configurable de intentos; un 400, un 404 o un 409 se devuelven como `EventosClientException` con el estado y el detalle del problema. Un conflicto de estado no es un fallo transitorio y no se disfraza de reintento. Agotados los intentos, o en una operación que no se reintenta, un fallo de transporte —incluido el tiempo de espera del `HttpClient`— sale como `EventosClientException` con `Estado` nulo y la excepción original como interna; todas las operaciones usan el mismo tipo de error.
5. **El transporte lo pone el consumidor.** El cliente recibe un `HttpClient` con `BaseAddress`, sin registro propio de `IHttpClientFactory`: la dirección, el tiempo de espera, la autenticación y el proxy son configuración de la aplicación.
6. **La publicación se prueba contra un doble del contrato.** Las pruebas fijan ruta, sobre, conservación de la carga, reintento con el mismo identificador y traducción de errores; no necesitan la API real ni su base de datos.

## Alternativas descartadas

- **Generar el cliente desde OpenAPI.** La API no publica un documento OpenAPI estable y el contrato son cuatro rutas; el generador añadiría dependencias y tipos que no aportan. Si el contrato crece, se replantea.
- **Reintentar todas las operaciones con una política común.** Es la opción cómoda y la que produce dobles leases y dobles cierres. La política se limita a la operación idempotente.
- **Aceptar la carga como `string`.** Deja al consumidor serializar y escapar a mano, y el sobre puede llevar JSON inválido. `JsonElement` conserva el documento tal como llega.
- **Validar la carga en el cliente.** Duplicaría las reglas del servidor y daría dos veredades sobre el mismo contrato.
