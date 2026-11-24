# Línea base de seguridad por aplicación

Una aplicación del portfolio se entrega como una casa con las puertas puestas: los controles no dependen de que alguien se acuerde de montarlos, sino de que vienen con el esqueleto. Este documento dice cuáles son, quién los mantiene y cómo se comprueba que están.

> **Estado:** Runbook preparado (2026-11-24). Cada control existe en el repositorio que lo mantiene; los de los paquetes tienen prueba propia y los de configuración se comprueban en su repositorio. La comprobación conjunta, `check-linea-base`, está pendiente. La política por permiso y el emisor local llegan con `LL.Security` 0.4.0, pendiente de publicar.
>
> **Para quién es:** para quien crea una aplicación y para quien revisa una existente.
>
> **Qué resuelve:** qué debe tener toda aplicación, dónde vive cada control, cómo se monta y cómo se prueba. No sustituye al `AGENTS.md` de cada repositorio: lo enlaza.

## La regla

Un control se implementa **una vez**, en el repositorio dueño del artefacto: el paquete en este repositorio, el CI en [`core`](https://github.com/lanuzalorenzo/core), el esqueleto en [`plantillas-dotnet`](https://github.com/lanuzalorenzo/plantillas-dotnet). Si el mismo código de seguridad aparece en dos aplicaciones, es un defecto. Qué se puede extraer y con qué criterio está en [ADR 0001](adr/0001-scope-and-extraction-criteria.md).

Las opciones de la plantilla deciden qué se crea. Nunca hay una opción de ejecución que desactive la seguridad: una bandera para apagar la autenticación acaba en producción.

## Los controles

| Id | Control | Dónde vive | Cómo se monta | Cómo se prueba |
|---|---|---|---|---|
| `auth` | Token de Entra ID | `LL.Security` | `AddHardenedJwtBearer` con emisor y audiencia obligatorios; sin valores, el arranque falla | 401 sin token; 401 con emisor, audiencia, clave o vigencia ajenos |
| `permisos` | Permiso por rol | `LL.Security` | `AddPermissionPolicies` y, en el endpoint, `RequirePermission("orders.read")`; `UseAuthentication` y `UseAuthorization` en el pipeline; el claim es una opción: `permissions` por defecto y `ClaimTypes.Role` con roles de Entra ID y el mapeo de claims por defecto | 403 con token válido y sin el permiso; 200 con el permiso |
| `cors` | CORS | `LL.Security` | `AddDenyByDefaultCors` con la sección de orígenes y `UseCors` con el nombre de la política; sin lista, ningún origen pasa | origen no permitido rechazado |
| `cabeceras` | Cabeceras de seguridad | `LL.Security` en API; `nginx.conf` en web | `UseSecurityHeaders` en la API, o la configuración del servidor en la web | cabeceras presentes y `Server` ausente |
| `csp` | Política de contenido | `LL.Security` | `SecurityHeadersOptions.Csp` con una `ContentSecurityPolicy` | cabecera presente y sin `unsafe-inline` |
| `limite` | Límite de peticiones | `LL.Security` | `AddRemoteIpRateLimiting` con umbral y ventana, y `UseRateLimiter()`; particiona por IP salvo que el consumidor cambie `PartitionKey` | peticiones por encima del umbral rechazadas |
| `problemdetails` | Errores con formato estable | `LL.Hosting` | `AddApiProblemDetails` y `UseApiProblemDetails` | error con `application/problem+json` |
| `salud` | Sonda | `LL.Hosting` | `AddApiHealthChecks` y `MapApiHealthChecks`, patrón `/health` por defecto y parametrizable | `/health` anónimo y sin detalle de dependencias |
| `permisos-workflow` | Permisos mínimos del CI | workflow del repositorio | `permissions:` explícito y mínimo por trabajo | sin `write-all`; `sast` con `security-events: write` |
| `secretos` | Secretos fuera del repo | convención, hook y CI | llavero del sistema o identidad administrada; `appsettings.Local.json` ignorado en la plantilla | Gitleaks sobre el historial y revisión del lote |
| `sast` | Análisis estático | [`core/actions/sast@v1.8`](https://github.com/lanuzalorenzo/core/tree/v1.8/actions/sast) | paso del workflow | sin hallazgos altos |
| `sca` | Dependencias e imagen | [`core/actions/scan@v1.8`](https://github.com/lanuzalorenzo/core/tree/v1.8/actions/scan) | paso del workflow | Trivy sin vulnerabilidades altas |
| `contenedor` | Contenedor sin privilegios | plantilla `ll-api` o `ll-blazor` de [`plantillas-dotnet`](https://github.com/lanuzalorenzo/plantillas-dotnet) | Dockerfile generado | la imagen no corre como root |
| `dependabot` | Actualizaciones | plantilla de `plantillas-dotnet` | configuración generada | auto-merge de parche y menor con el pipeline en verde |
| `main` | Rama protegida | repositorio | rama y pull request, checks obligatorios | no hay push directo; merge solo con checks |
| `iac` | Infraestructura como código | repositorio que la tenga | Bicep o Terraform con parámetros, sin identificadores | `build` del IaC en verde y análisis sin hallazgos |

## Qué es obligatorio según el artefacto

| Artefacto | Controles obligatorios |
|---|---|
| API | `auth`, `permisos`, `cors`, `cabeceras`, `csp`, `limite`, `problemdetails`, `salud`, `permisos-workflow`, `secretos`, `sast`, `sca`, `main`, `dependabot` |
| Web | `cabeceras`, `permisos-workflow`, `secretos`, `sast`, `sca`, `main`, `dependabot` |
| Paquete | `permisos-workflow`, `secretos`, `sast`, `sca`, `main`, `dependabot` |
| Con Docker | los de su artefacto más `contenedor` |
| Con infraestructura | los de su artefacto más `iac` |

## La autenticación, paso a paso

1. Registro de aplicación en Entra ID con un rol por permiso.
2. Interfaz de usuario, MSAL en el cliente; servicio a servicio, credenciales de cliente o identidad administrada.
3. En la API, `AddHardenedJwtBearer`, `AddPermissionPolicies` y `UseAuthentication`/`UseAuthorization` en el pipeline, con un permiso por rol. Sin valores, no arranca.
4. En local, `dotnet user-jwts` para prueba manual, o `AddLocalJwtBearer` con un `LocalJwtIssuer` para las pruebas de contrato: mismo `JwtBearer`, sin autoridad remota y con una clave simétrica en memoria.
5. Pruebas de contrato mínimas: 401 sin token; 401 con emisor, audiencia, clave o vigencia ajenos; 403 con el token correcto y sin el permiso; 200 con el permiso; `/health` anónimo.

El modelo está probado en [comicteca](https://github.com/lanuzalorenzo/comicteca) —API y web con Entra ID, con diez pruebas de contrato según su tabla de capacidades— y en [lab-api-local-azure](https://github.com/lanuzalorenzo/lab-api-local-azure), que valida tokens reales de un tenant.

## Cómo lo declara una aplicación

Cada aplicación añade a su `AGENTS.md` una sección `## Línea base` con una fila por control aplicable, usando los identificadores de la tabla:

```markdown
## Línea base

| Id | Estado | Evidencia |
|---|---|---|
| `auth` | Validado | diez pruebas de contrato; CI del 14/11 |
| `permisos` | Validado | 403 con la cuenta no permitida |
| `iac` | Pendiente | sin infraestructura todavía |
```

Estados: los cinco del workspace —`Validado`, `Parcial`, `Runbook preparado`, `Diseño` y `Pendiente`—, con la evidencia o lo que falta. Un control que no aplica no se declara: se omite, y la lista obligatoria de su artefacto dice si eso es aceptable.

Un estado `Validado` sin evidencia no vale. Si la comprobación es de otra fecha o de otro entorno, se dice.

## Verificación

- Las pruebas de contrato del propio repositorio, en su `AGENTS.md` §Verificación.
- `check-linea-base` de `core` (pendiente): leerá la sección `## Línea base` y el workflow, y fallará si falta un control obligatorio, si el estado no es uno de los cinco o si un `Validado` no trae evidencia.
- Gitleaks sobre el historial completo en cada push.

## Lo que no entra

- Configuración de un consumidor concreto dentro de un paquete: lo que cambia por aplicación entra por opciones y se valida al arrancar.
- Reglas de negocio, datos o dominio.
- Un modo de ejecución sin autenticación, aunque sea de desarrollo.
- Estados nuevos: si una etiqueta de presentación no coincide, se declara su equivalencia en el `AGENTS.md` del repositorio, no se inventa un sexto estado.
