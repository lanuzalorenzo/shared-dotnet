using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Security;

/// <summary>
/// Ajustes de la política CORS. Sin orígenes configurados, la política no
/// permite ninguno: la denegación es el valor por defecto.
/// </summary>
public sealed class DenyByDefaultCorsOptions
{
    /// <summary>Nombre con el que el host aplica la política en <c>UseCors</c>.</summary>
    public string PolicyName { get; set; } = "ApiCors";

    /// <summary>Sección de configuración con la lista de orígenes permitidos.</summary>
    public string ConfigurationSection { get; set; } = "Cors:AllowedOrigins";

    public bool AllowAnyHeader { get; set; } = true;

    public bool AllowAnyMethod { get; set; } = true;
}

public static class CorsExtensions
{
    /// <summary>
    /// Registra una política CORS que solo permite los orígenes de la sección de
    /// configuración indicada. Si la lista está vacía o falta, ningún origen pasa.
    /// </summary>
    public static IServiceCollection AddDenyByDefaultCors(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DenyByDefaultCorsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new DenyByDefaultCorsOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.PolicyName))
        {
            throw new ArgumentException("PolicyName no puede estar vacío.", nameof(configure));
        }

        if (string.IsNullOrWhiteSpace(options.ConfigurationSection))
        {
            throw new ArgumentException("ConfigurationSection no puede estar vacío.", nameof(configure));
        }

        services.AddCors(cors => cors.AddPolicy(options.PolicyName, policy =>
        {
            // La sección se lee al construir la política, no al registrar el servicio:
            // los proveedores que añade el host de pruebas solo existen después de Build.
            var origins = configuration.GetSection(options.ConfigurationSection).Get<string[]>() ?? [];

            if (origins.Length == 0)
            {
                policy.SetIsOriginAllowed(_ => false);
                return;
            }

            policy.WithOrigins(origins);

            if (options.AllowAnyHeader)
            {
                policy.AllowAnyHeader();
            }

            if (options.AllowAnyMethod)
            {
                policy.AllowAnyMethod();
            }
        }));

        return services;
    }
}
