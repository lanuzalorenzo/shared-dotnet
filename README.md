<img src="assets/iconos/shared-dotnet.svg" width="64" alt="Icono de shared-dotnet">

# Controles compartidos para APIs ASP.NET Core

Dos paquetes NuGet con los controles que repiten todas las APIs de este portfolio: cabeceras de seguridad, CORS cerrado por defecto, validación de tokens, límite por IP, ProblemDetails, HTTPS con HSTS y endpoint de salud. La lógica vive aquí una vez; cada API la instala y la fija por versión.

> **Estado (2026-11-05).** Paquetes en `0.1.0`: compilan y sus pruebas pasan en local. La publicación en el feed está preparada y no ejecutada. El consumo desde `EcommerceApi` y `OrderProcessor` está pendiente.

Un control que vive en un documento depende de que cada equipo lo lea y lo copie bien. Uno que vive en un paquete se instala, se versiona y se audita una sola vez. Con un servicio la diferencia es pequeña; con veinte, es la diferencia entre revisar veinte copias a mano y revisar el original.

## Paquetes

| Paquete | Qué aporta | Control que sustituye |
|---|---|---|
| `LL.Security` | Cabeceras de seguridad, CORS sin orígenes por defecto, JWT endurecido y límite por IP | OWASP API8 y API4 |
| `LL.Hosting` | ProblemDetails, redirección a HTTPS con HSTS y salud anónima | Errores sin formato estable y sonda con token |

El prefijo `LL` es el monograma —dos eles—, no una abreviatura suelta. El feed de GitHub Packages añade el propietario a la ruta, así que un identificador corto no queda expuesto a que otro publicador use el mismo.

Cada uno lleva sus propias pruebas: el paquete no se apoya en las del consumidor.

## Cómo se usa

```csharp
builder.Services.AddDenyByDefaultCors(builder.Configuration);
builder.Services.AddHardenedJwtBearer(options =>
{
    options.Authority = builder.Configuration["AzureAd:Authority"]!;
    options.Audience = builder.Configuration["AzureAd:Audience"]!;
});
builder.Services.AddRemoteIpRateLimiting();
builder.Services.AddApiProblemDetails();
builder.Services.AddApiHealthChecks();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSecurityHeaders();
app.UseApiProblemDetails();
app.UseApiTransportSecurity(app.Environment);
app.UseCors("ApiCors");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapApiHealthChecks();
```

El orden importa: las cabeceras se aplican cuando la respuesta empieza a escribirse, así que también llegan a los 500 que genera el manejador de excepciones; el manejador va antes que el resto del pipeline de la aplicación.

`AddHardenedJwtBearer` exige emisor y audiencia reales y falla al arrancar si faltan. Un servicio que arranca con marcadores y descubre el fallo en la primera petición autenticada es un servicio que ya está en producción mal configurado. La prueba del paquete cubre esa configuración; la validación efectiva de un token la ejerce el consumidor contra su emisor.

## Criterio: qué entra y qué no

Un control entra en un paquete cuando se cumple una de dos condiciones:

1. Lo necesita un segundo consumidor real.
2. Su valor por defecto es una decisión de seguridad, y se extrae a la primera para que nadie la reimplemente mal.

No entra nada con reglas de negocio ni acceso a datos. `EcommerceApi.Domain`, la persistencia y los casos de uso se quedan en la API: son de esa aplicación, no de la plataforma. El detalle está en [ADR 0001](docs/adr/0001-scope-and-extraction-criteria.md).

## Versionado

- Cada paquete tiene su versión. No se publica el repositorio entero con un número.
- Una versión publicada no se reescribe: un cambio incompatible sube el número mayor del paquete y deja nota de migración.
- El consumidor fija un rango (`0.1.*` mientras no haya compromiso de estabilidad) y el pipeline sube la referencia.

El criterio completo está en [ADR 0002](docs/adr/0002-versioning-and-feed.md).

## Publicación

Los paquetes van a GitHub Packages, en el feed del propietario del repositorio. No hace falta cuenta externa ni clave de larga vida: el pipeline usa el token del propio flujo, con permiso `packages: write` y solo en el job de publicación.

El consumidor añade el feed y las credenciales en su `nuget.config`:

```xml
<packageSources>
  <add key="github" value="https://nuget.pkg.github.com/lanuzalorenzo/index.json" protocolVersion="3" />
</packageSources>
<packageSourceCredentials>
  <github>
    <add key="Username" value="%GITHUB_ACTOR%" />
    <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
  </github>
</packageSourceCredentials>
```

GitHub Packages exige autenticación incluso para leer paquetes públicos. Si algún día el paquete debe instalarse sin credenciales, el destino es nuget.org, con el prefijo reservado y una decisión escrita.

## Verificación

```bash
dotnet restore SharedDotnet.sln
dotnet build SharedDotnet.sln -c Release --no-restore
dotnet test SharedDotnet.sln -c Release --no-build --no-restore
dotnet format SharedDotnet.sln --verify-no-changes --no-restore
```

El CI reutiliza el workflow `dotnet.yml` de `lanuzalorenzo/core`, que ejecuta esos mismos pasos. Un cambio está listo cuando el pipeline pasa en verde.

Licencia: MIT.
