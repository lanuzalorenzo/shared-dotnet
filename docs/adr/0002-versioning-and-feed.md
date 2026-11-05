# 0002 — Versionado por paquete y feed de publicación

- Estado: aceptado
- Fecha: 2026-11-05

## Contexto

Un paquete NuGet no se comporta como un workflow: la versión que se publica queda fija y no se puede repuntar. Un consumidor que fija `1.0.0` recibe exactamente ese binario; no hay etiqueta móvil que actualice.

## Decisión

- **Versión por paquete.** El tag `security/v0.1.0` publica `LL.Security` y no toca `LL.Hosting`. El repositorio no tiene una versión única.
- **Versiones inmutables.** Una versión publicada no se reescribe. Un cambio incompatible sube el número mayor del paquete afectado y deja nota de migración en el README.
- **Consumo con rango.** Mientras no haya compromiso de estabilidad, el consumidor fija `0.1.*` y el pipeline sube la referencia cuando aparece una versión nueva.
- **Feed GitHub Packages.** El paquete se publica en el feed del propietario, con `GITHUB_TOKEN` y `packages: write`, solo en el job de publicación. No se guardan claves de NuGet en secretos.

## Consecuencias

- Cada paquete puede romper compatibilidad sin arrastrar a los demás.
- GitHub Packages exige autenticación incluso para paquetes públicos; un consumidor externo necesita token. Publicar en nuget.org es una decisión aparte, con prefijo reservado y paquete que no se puede borrar.
- Publicar deja de ser un efecto secundario del merge: exige un tag deliberado.
