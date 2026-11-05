using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LL.Security;

/// <summary>
/// Cabeceras de seguridad que el middleware añade a todas las respuestas.
/// </summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>
    /// Cabeceras y valores. Se pueden sobrescribir o añadir antes de registrar el middleware.
    /// </summary>
    public IDictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "no-referrer",
            ["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()",
        };
}

/// <summary>
/// Middleware que escribe las cabeceras de seguridad en la respuesta.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Añade las cabeceras configuradas a cada respuesta. Se registran para cuando
    /// la respuesta empieza a escribirse y no antes, porque el manejador de
    /// excepciones limpia las cabeceras al convertir un fallo en respuesta.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var headers = options ?? new SecurityHeadersOptions();

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                foreach (var header in headers.Headers)
                {
                    context.Response.Headers.TryAdd(header.Key, header.Value);
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
