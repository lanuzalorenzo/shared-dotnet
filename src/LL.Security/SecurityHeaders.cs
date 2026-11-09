using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Política de seguridad de contenido. Si no se define, no se escribe la
    /// cabecera. No se admite a la vez que una CSP en <see cref="Headers"/>.
    /// </summary>
    public ContentSecurityPolicy? Csp { get; set; }

    /// <summary>
    /// Apaga la cabecera <c>Server</c> de Kestrel, que por defecto anuncia el
    /// servidor web. Activado por defecto; ponlo a <c>false</c> si el host
    /// necesita publicarla. No afecta a hosts que no sean Kestrel.
    /// </summary>
    public bool SuppressServerHeader { get; set; } = true;
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
    /// Además apaga la cabecera <c>Server</c> de Kestrel salvo que el consumidor
    /// lo impida con <see cref="SecurityHeadersOptions.SuppressServerHeader"/>;
    /// si el host no usa Kestrel, no hay nada que apagar.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var headers = options ?? new SecurityHeadersOptions();

        if (headers.SuppressServerHeader)
        {
            var kestrelOptions = app.ApplicationServices.GetService<IOptions<KestrelServerOptions>>();
            if (kestrelOptions is not null)
            {
                kestrelOptions.Value.AddServerHeader = false;
            }
        }

        var policy = headers.Csp;
        string? cspHeaderName = null;

        if (policy is not null)
        {
            if (policy.IsEmpty)
            {
                throw new InvalidOperationException(
                    "La política de contenido no tiene directivas. Define al menos una o no la registres.");
            }

            if (headers.Headers.ContainsKey("Content-Security-Policy")
                || headers.Headers.ContainsKey("Content-Security-Policy-Report-Only"))
            {
                throw new InvalidOperationException(
                    "La cabecera de política de contenido está en Headers y en Csp. Deja una sola fuente.");
            }

            cspHeaderName = policy.ReportOnly
                ? "Content-Security-Policy-Report-Only"
                : "Content-Security-Policy";
        }

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                foreach (var header in headers.Headers)
                {
                    context.Response.Headers.TryAdd(header.Key, header.Value);
                }

                if (cspHeaderName is not null && policy is not null)
                {
                    context.Response.Headers.TryAdd(
                        policy.ReportOnly
                            ? "Content-Security-Policy-Report-Only"
                            : "Content-Security-Policy",
                        policy.ToHeaderValue());
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
