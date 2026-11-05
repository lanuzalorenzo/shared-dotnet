using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace LL.Hosting;

/// <summary>
/// Ajustes de transporte. El entorno de pruebas no redirige ni envía HSTS: los
/// clientes de prueba hablan HTTP y una redirección les rompería la petición.
/// </summary>
public sealed class TransportSecurityOptions
{
    /// <summary>Entorno que se salta la redirección y HSTS.</summary>
    public string SkipEnvironmentName { get; set; } = "Testing";

    /// <summary>HSTS en todos los entornos salvo desarrollo.</summary>
    public bool EnableHstsOutsideDevelopment { get; set; } = true;
}

public static class TransportSecurityExtensions
{
    /// <summary>
    /// Redirige a HTTPS y, fuera de desarrollo, envía HSTS. Se salta por completo
    /// en el entorno configurado en <see cref="TransportSecurityOptions.SkipEnvironmentName"/>.
    /// </summary>
    public static IApplicationBuilder UseApiTransportSecurity(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        Action<TransportSecurityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new TransportSecurityOptions();
        configure?.Invoke(options);

        if (environment.IsEnvironment(options.SkipEnvironmentName))
        {
            return app;
        }

        app.UseHttpsRedirection();

        if (options.EnableHstsOutsideDevelopment && !environment.IsDevelopment())
        {
            app.UseHsts();
        }

        return app;
    }
}
