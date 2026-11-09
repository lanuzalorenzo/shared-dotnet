# 0005 — La cabecera `Server` se apaga desde `LL.Security`

- Estado: aceptado
- Fecha: 2026-11-09

## Contexto

Kestrel añade `Server: Kestrel` a todas las respuestas salvo que se apague. El valor no aporta nada al cliente y confirma qué servidor web hay detrás, que es información útil para elegir un exploit. Un consumidor real lo corrigió en su `Program.cs` con una línea; cualquier otra API que monte `UseSecurityHeaders` repetiría el hallazgo, y la plantilla `ll-api` lo heredaría sin saberlo.

`UseSecurityHeaders` ya es el punto donde el paquete concentra las cabeceras de respuesta. La cabecera `Server` la escribe el servidor, no el middleware, así que el sitio de la decisión es el mismo aunque el mecanismo sea distinto: se toca `KestrelServerOptions` antes de que el servidor arranque.

## Decisión

1. **La apaga el paquete, con opción de deshacerlo.** `SecurityHeadersOptions.SuppressServerHeader` vale `true` por defecto y el consumidor puede ponerlo a `false`. Entra por opciones, como el resto de la configuración del paquete.
2. **Se aplica en `UseSecurityHeaders`, no en una llamada nueva.** La llamada que ya existe apaga la cabecera; no se añade un `AddHardenedKestrel` que el consumidor tenga que recordar y que la plantilla podría no llevar.
3. **Solo alcanza a Kestrel.** Escribe en las opciones de Kestrel del host; si detrás hay IIS, un nginx o una plataforma que fija sus propias cabeceras, la `Server` la pone ese componente y este control no la toca. Si no hay opciones de Kestrel registradas, la llamada no falla.
4. **Apagado gana a encendido.** El paquete solo escribe `false`; una segunda llamada con `SuppressServerHeader = false` no vuelve a encender la cabecera, así que el resultado no depende del orden entre llamadas.
5. **Gana el defecto seguro.** Un consumidor que publique a propósito `Server` tiene que escribir la excepción; al revés, cada proyecto nuevo nacería con la huella puesta.

## Alternativas descartadas

- **Dejarlo en cada consumidor.** Es lo que había: una línea por API, sin prueba y visible solo si alguien mira las cabeceras.
- **Ponerlo en `LL.Hosting`, junto al transporte.** `LL.Hosting` ya configura HTTPS y HSTS, pero eso es transporte: el esquema y la redirección. Aquí la decisión es qué anuncia la respuesta, y las demás cabeceras ya las concentra `UseSecurityHeaders`; además, `LL.Hosting` obligaría a ese paquete a conocer Kestrel para una sola línea y el consumidor tendría dos llamadas que mantener en el mismo orden.
- **Una llamada de configuración del host en `LL.Security`.** Separaría dos cosas que el consumidor registra juntas y dejaría la plantilla con una llamada más que sincronizar.
- **Un middleware que borre la cabecera.** Llega tarde: Kestrel la escribe al enviar la respuesta y el middleware tendría que reescribir cabeceras ya empezadas.
- **Apagarla sin opción.** Rompe la regla de que todo entra por opciones y quita al consumidor la última palabra sobre su propio host.

## Consecuencias

- `LL.Security` pasa a `0.3.0`; el consumidor sube la versión y borra su línea local. Un `ConfigureKestrel(o => o.AddServerHeader = true)` del consumidor queda pisado salvo que use `SuppressServerHeader = false`.
- La prueba del paquete arranca Kestrel real en loopback y lee la respuesta: comprueba la ausencia de la cabecera y la opción de deshacerla. `TestServer` no sirve para eso porque nunca la escribe; el pipeline del consumidor la vuelve a comprobar contra su contenedor.
- Solo cubre Kestrel. Un nginx, un proxy o una plataforma delante pueden añadir su propia cabecera y este control no los ve.
