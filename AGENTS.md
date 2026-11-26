# Guía para agentes: shared-dotnet

Paquetes NuGet con los controles que comparten las APIs del portfolio. Este repositorio no contiene la aplicación: contiene lo que la aplicación instala. Si un cambio necesita conocer el dominio de un consumidor, va mal.

## Estructura

- `src/LL.Security/`: cabeceras, CORS, JWT y límite por IP.
- `src/LL.Hosting/`: ProblemDetails, transporte y salud.
- `src/LL.Eventos.Client/`: cliente del contrato v1 de la cola de eventos.
- `src/LL.Eventos.Worker/`: bucle de consumo del contrato v1, con concurrencia acotada, apagado que devuelve el lease y sonda de atasco.
- `src/LL.Observabilidad/`: nombres de trazas y métricas, correlación por `evento.id`, propagación W3C entre publicar y ack y registro que no copia la carga.
- `tests/`: una prueba por contrato. Los controles de servidor usan `WebApplication` y `TestServer`, y arrancan Kestrel real cuando la cabecera la escribe el servidor; los clientes HTTP usan un `HttpMessageHandler` doble. Cada proyecto de pruebas prueba su paquete, no el consumidor.
- `docs/adr/`: decisiones estructurales. Toda decisión de este tipo se escribe antes de cambiar el código.
- `docs/linea-base.md`: qué debe tener toda aplicación, con cada control, su dueño, cómo se monta y cómo se prueba.
- `assets/iconos/`: icono del README, con su especificación.

## Reglas

- Sin secretos, credenciales ni datos personales. Los ejemplos usan valores ficticios.
- La auditoría de dependencias falla la compilación ante cualquier vulnerabilidad conocida: `NuGetAudit` en modo `all` y `NU1901`–`NU1904` como errores.
- Dependabot mantiene NuGet y GitHub Actions; una actualización de parche o menor se fusiona sola si el pipeline pasa.
- Sin configuración de un consumidor concreto dentro de un paquete: todo entra por opciones y se valida en el registro.
- Un control nuevo entra con su prueba. Un paquete que no puede probar lo que promete no se publica.
- Las comprobaciones de seguridad no se relajan para que una prueba pase. Si una prueba no puede ejercer el comportamiento, se cambia la prueba.
- **Línea base:** este repositorio publica controles; no declara `## Línea base` porque no es una aplicación. Qué debe tener una aplicación está en `docs/linea-base.md`.
- El identificador del paquete es `LL.<Capacidad>` y el espacio de nombres coincide. No se crean paquetes de conveniencia que envuelvan una línea.
- `Directory.Packages.props` es la única fuente de versiones de dependencias.
- La versión de .NET, las actualizaciones mayores y las dependencias directas y transitivas siguen la [política de plataforma](https://github.com/lanuzalorenzo/core/blob/main/docs/versiones-y-dependencias.md): LTS, mayores de la familia bloqueados y pin de transitivas en el centro.

## Verificación

```bash
dotnet restore SharedDotnet.sln
dotnet build SharedDotnet.sln -c Release --no-restore
dotnet test SharedDotnet.sln -c Release --no-build --no-restore
dotnet format SharedDotnet.sln --verify-no-changes --no-restore
gitleaks detect --source . --redact --verbose --exit-code 2
```

El CI llama al workflow `dotnet.yml` de `lanuzalorenzo/core` y escanea secretos con Gitleaks sobre el historial completo. El hook local se instala con `scripts/install-git-hooks.sh` y se comprueba con `--check`; es un aviso temprano, no sustituye al CI. Un cambio está listo cuando el pipeline pasa en verde y los comandos anteriores no fallan.

## Versionado y publicación

- Un tag `security/vX.Y.Z`, `hosting/vX.Y.Z`, `eventos/vX.Y.Z`, `worker/vX.Y.Z` u `observabilidad/vX.Y.Z` publica un solo paquete. No hay versión de repositorio.
- Una versión publicada no se borra ni se reescribe. Un cambio incompatible sube el mayor y deja nota de migración.
- La publicación usa `GITHUB_TOKEN` con `packages: write`, solo en ese job. No se guardan claves de NuGet en secretos.
- Antes de publicar la primera versión, comprobar que el número es el que se quiere: en un feed público no se puede retirar del todo.

## Commits

Formato: `type(scope): imperative summary in English`. Tipos: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`. Scopes: `security`, `hosting`, `eventos`, `worker`, `observabilidad`, `ci`, `docs`, `repo`.

Los cambios entran por rama y pull request. El commit inicial se hizo sobre `main` porque el repositorio aún no tenía ramas; es la única excepción y no se repite.
