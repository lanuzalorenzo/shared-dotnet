# Guía para agentes: shared-dotnet

Paquetes NuGet con los controles que comparten las APIs del portfolio. Este repositorio no contiene la aplicación: contiene lo que la aplicación instala. Si un cambio necesita conocer el dominio de un consumidor, va mal.

## Estructura

- `src/LL.Security/`: cabeceras, CORS, JWT y límite por IP.
- `src/LL.Hosting/`: ProblemDetails, transporte y salud.
- `tests/`: una prueba por contrato, con `WebApplication` y `TestServer`. Cada proyecto de pruebas prueba su paquete, no el consumidor.
- `docs/adr/`: decisiones estructurales. Toda decisión de este tipo se escribe antes de cambiar el código.
- `assets/iconos/`: icono del README, con su especificación.

## Reglas

- Sin secretos, credenciales ni datos personales. Los ejemplos usan valores ficticios.
- La auditoría de dependencias falla la compilación ante cualquier vulnerabilidad conocida: `NuGetAudit` en modo `all` y `NU1901`–`NU1904` como errores.
- Dependabot mantiene NuGet y GitHub Actions; una actualización de parche o menor se fusiona sola si el pipeline pasa.
- Sin configuración de un consumidor concreto dentro de un paquete: todo entra por opciones y se valida en el registro.
- Un control nuevo entra con su prueba. Un paquete que no puede probar lo que promete no se publica.
- Las comprobaciones de seguridad no se relajan para que una prueba pase. Si una prueba no puede ejercer el comportamiento, se cambia la prueba.
- El identificador del paquete es `LL.<Capacidad>` y el espacio de nombres coincide. No se crean paquetes de conveniencia que envuelvan una línea.
- `Directory.Packages.props` es la única fuente de versiones de dependencias.

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

- Un tag `security/vX.Y.Z` o `hosting/vX.Y.Z` publica un solo paquete. No hay versión de repositorio.
- Una versión publicada no se borra ni se reescribe. Un cambio incompatible sube el mayor y deja nota de migración.
- La publicación usa `GITHUB_TOKEN` con `packages: write`, solo en ese job. No se guardan claves de NuGet en secretos.
- Antes de publicar la primera versión, comprobar que el número es el que se quiere: en un feed público no se puede retirar del todo.

## Commits

Formato: `type(scope): imperative summary in English`. Tipos: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`. Scopes: `security`, `hosting`, `ci`, `docs`, `repo`.

Los cambios entran por rama y pull request. El commit inicial se hizo sobre `main` porque el repositorio aún no tenía ramas; es la única excepción y no se repite.
