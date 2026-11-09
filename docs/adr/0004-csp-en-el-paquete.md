# 0004 — La política de contenido entra en `LL.Security`

- Estado: aceptado
- Fecha: 2026-11-09

## Contexto

Una API sin `Content-Security-Policy` deja que el navegador cargue cualquier origen si algún día sirve HTML, y no cierra el marco ante un incrustado ajeno. La cabecera se escribe en cuatro líneas de C# y por eso nadie la prueba: un `default-src` que falta o una directiva con una errata no rompe la compilación y no aparece en ninguna prueba.

El consumidor real hoy la escribe a mano en su `Program.cs`, dentro de un middleware propio que comprueba si la cabecera ya existe. La web de ese mismo consumidor tiene otra política, más amplia, servida por su servidor estático. Son dos políticas distintas y las dos correctas: una API JSON no necesita cargar nada, una aplicación web sí.

## Decisión

`LL.Security` incorpora `ContentSecurityPolicy` y la opción `SecurityHeadersOptions.Csp`:

1. **La política la elige el consumidor.** El paquete no impone una política por defecto: la de una API y la de una web no coinciden, y una por defecto sería la política equivocada en la mitad de los casos. `ContentSecurityPolicy.DenyAll()` es un atajo documentado para el caso de la API JSON.
2. **Se compone por directivas, no por una cadena suelta.** `Add(directiva, valores)` valida el nombre y los valores, rechaza `;` y saltos de línea, y serializa en orden de inserción. La cadena final se puede probar por partes.
3. **`ReportOnly` cambia la cabecera, no la política.** La misma composición sirve para observar antes de bloquear.
4. **La cabecera que ya escribió la respuesta manda.** El middleware usa `TryAdd`, así que una política puesta por el consumidor en ese punto no se sobrescribe.
5. **Dos fuentes para la misma cabecera son un error de arranque.** Si `Headers` ya contiene `Content-Security-Policy` o su variante de informe y además se define `Csp`, el registro falla. Elegir una en silencio deja al consumidor creyendo que aplica la otra.

## Alternativas descartadas

- **Dejarlo en el consumidor.** Es lo que hay: cuatro líneas sin prueba y repetidas en cada API que sirva algo más que JSON. El criterio de ADR-0001 lo admite por el segundo supuesto —es una decisión de seguridad con valor por defecto—, y un consumidor real ya la escribe a mano.
- **Una política por defecto en el paquete.** Obligaría a cada web a deshacerla y convertiría un control en una discusión sobre qué permite. El paquete valida y serializa; el consumidor decide.
- **Una cadena con la política completa como entrada.** Es un `Headers` con otro nombre: no valida, no compone y no permite probar una directiva sin comparar la cadena entera.
- **Un middleware propio.** `UseSecurityHeaders` ya escribe cabeceras al empezar la respuesta, y así la CSP también sobrevive al manejador de excepciones. Un segundo middleware repetiría ese orden.

## Consecuencias

- `LL.Security` pasa a `0.2.0`. Es aditivo: quien no defina `Csp` recibe las mismas cabeceras que antes.
- El consumidor que hoy la escribe a mano la sustituye por `options.Csp = ContentSecurityPolicy.DenyAll()` y borra su middleware; su web conserva la suya en el servidor estático.
- La política de una web sigue fuera del paquete: la sirve su servidor, no la aplicación .NET.
- Un cambio en la composición o en la validación lleva su prueba en `LL.Security.Tests`, como el resto de controles.
