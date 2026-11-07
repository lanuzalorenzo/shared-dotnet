# 0003 — Las plantillas no viven aquí

- Estado: aceptado
- Fecha: 2026-11-06

## Contexto

Hacen falta plantillas de `dotnet new` —una API, una aplicación Blazor y un paquete NuGet— que creen un repositorio con la línea base ya puesta: CI que llama a `core`, `.editorconfig`, `Directory.Packages.props`, Dockerfile y los controles que publica este repositorio.

La opción barata es meterlas aquí: ya se publica en NuGet y ya hay CI. Pero este repositorio tiene un alcance escrito (ADR-0001) y un versionado por paquete (`security/vX.Y.Z`, `hosting/vX.Y.Z`), y una plantilla no es un control que la aplicación instala: es el esqueleto que la crea.

## Decisión

Las plantillas van a un repositorio público propio, `plantillas-dotnet`. Este repositorio no las publica.

Motivos:

1. **Artefacto y ciclo distintos.** Una plantilla se instala con `dotnet new install`; un control, con `dotnet add package`. Una se versiona por plantilla y el otro por paquete.
2. **Una plantilla conoce la forma del consumidor**: estructura de carpetas, CI, Dockerfile. ADR-0001 deja fuera lo que depende de esa forma.
3. **El contenido de una plantilla no es un control de seguridad** y no debe entrar en la auditoría de dependencias de los paquetes.

## Consecuencias

- El repositorio nuevo nace con la línea base pública antes del primer push: Gitleaks, Dependabot con auto-merge de parche y menor, permisos mínimos y `main` con rama y pull request.
- Las plantillas consumen este repositorio y `core` por versión publicada, nunca por ruta: `dotnet add package` y `uses: …@vX.Y`.
- Cambiar una plantilla no obliga a versionar ni publicar un paquete de controles, y al revés.
- Se rechaza publicarlas como paquetes `LL.*` aquí: unifica el feed a costa de mezclar dos ciclos y de romper el alcance de ADR-0001.
- Se crean tres plantillas separadas —`ll-api`, `ll-blazor` y `ll-nuget`— y las plantillas no se publican como paquete: cada una se instala con `dotnet new install` desde su carpeta.
