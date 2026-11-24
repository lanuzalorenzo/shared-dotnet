# 0006 — Políticas por permiso y emisor local de prueba en `LL.Security`

- Estado: aceptado
- Fecha: 2026-11-24

## Contexto

La línea base pide una política por permiso y una prueba que responda 403 con un token válido y sin el permiso. Hoy cada API escribe esa política a mano: registra el nombre en `AddAuthorization`, lo repite en el endpoint y, si los dos textos se separan, la política no existe y el permiso deja de comprobarse en silencio. El paquete valida el token, pero no ofrece la pieza que convierte un permiso en una negativa.

La prueba del 403 tiene el mismo problema desde el otro lado: para ejercerla hace falta un token con emisor, audiencia y firma válidos. Contra un tenant real la prueba depende de la red, de una cuenta y de un registro que no debe existir en el pipeline; contra nada, no se puede probar el permiso.

## Decisión

`LL.Security` incorpora dos piezas.

1. **Un permiso nombra una política.** `RequirePermission("orders.read")` sobre un endpoint exige la política `permission:orders.read`. El nombre se compone en un solo sitio y no se repite: el endpoint declara el permiso, no un nombre de política.
2. **Un proveedor sirve una política por permiso.** `PermissionPolicyProvider` extiende el proveedor por defecto y devuelve, para cualquier nombre con el prefijo, una política que exige usuario autenticado y el claim del permiso. Las políticas que el consumidor declara en `AuthorizationOptions` siguen resolviéndose. El prefijo `permission:` queda reservado: una política del consumidor con ese nombre es un error explícito, no una sustitución silenciosa, y el registro reemplaza al proveedor de políticas en vez de encadenarse con otro propio. No hay lista que mantener: una errata en el endpoint produce un 403, nunca una puerta abierta.
3. **El claim es una opción, por defecto `permissions`.** El paquete no adivina el emisor. Con roles de aplicación de Entra ID y el mapeo de claims por defecto, el consumidor declara `ClaimTypes.Role`; con un claim propio, el suyo. Un tipo vacío es un error de registro.
4. **El 401 y el 403 los pone el framework.** La política exige autenticación y claim; el desafío y la denegación salen del middleware de autorización, sin un manejador propio que pueda devolver otro código.
5. **El emisor local es una puerta explícita.** `AddLocalJwtBearer(LocalJwtIssuer)` monta el mismo `JwtBearer` con los mismos parámetros de validación, sin autoridad remota y con la clave simétrica del emisor. El emisor se construye en memoria, con una clave aleatoria de 256 bits por instancia y cinco minutos de vigencia por token. No es una bandera sobre `AddHardenedJwtBearer`: una opción que cambia el emisor dentro del mismo registro acaba copiada en producción, y una clave que solo existe en el proceso no valida nada fuera de él.
6. **Un nombre de permiso inválido falla al montar la aplicación.** Vacío o con espacios en los extremos es un error de registro, no una petición que se descubre en el primer 403.

Las dos piezas entran por [ADR 0001](0001-scope-and-extraction-criteria.md). La política por permiso, por el segundo supuesto, valor por defecto de seguridad: una política que no coincide con su endpoint deja el permiso sin comprobar. El emisor local, por el primero, segundo consumidor real: lo usan las pruebas de este paquete y las de contrato de cada aplicación, y sin él esa comprobación depende de un tenant.

## Alternativas descartadas

- **Registrar la lista de permisos a mano.** Es lo que ya hace cada consumidor. La lista y los endpoints se separan con el primer cambio, y la separación no rompe el arranque: rompe el control.
- **`[Authorize(Roles = "…")]`.** Depende del mapeo de claims del emisor, no se compone con otras políticas y deja el nombre del rol escrito en el atributo.
- **Un `RequireClaim` en cada endpoint.** Obliga a registrar la política igualmente y repite la condición en cada mapa.
- **Una bandera en `HardenedJwtBearerOptions` para usar una clave local.** Es el interruptor que la línea base prohíbe: un booleano mal puesto convierte la validación del tenant en la de una clave de prueba.
- **Un paquete `LL.Security.Testing`.** Sesenta líneas de emisor no justifican un segundo identificador, su versión y su publicación. La puerta explícita dentro del paquete deja el riesgo donde se ve.
- **`dotnet user-jwts`.** Sirve para probar a mano, no para una prueba de contrato en el pipeline: no es programable ni fija emisor y audiencia desde el proyecto de pruebas.

## Consecuencias

- `LL.Security` pasa a `0.4.0`. Es aditivo: quien no llame a las dos piezas nuevas no cambia de comportamiento.
- El consumidor sustituye su registro de políticas por `AddPermissionPolicies()`, marca el endpoint con `.RequirePermission(...)` y prueba el 403 con `AddLocalJwtBearer` y un token del emisor local.
- La fila `permisos` de `docs/linea-base.md` deja de decir que el helper está pendiente y pasa a nombrar el paquete como dueño.
- El emisor local no sustituye a la validación contra Entra ID: la prueba del 403 comprueba la autorización, no la federación. La validación del tenant se sigue probando en el consumidor.
- Un cambio en el nombre de la política o en el claim por defecto es incompatible y sube el mayor con nota de migración.
