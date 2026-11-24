using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LL.Security;

/// <summary>
/// Cómo se reconoce un permiso dentro de la identidad. El paquete no adivina el
/// emisor: con un claim propio se deja el valor por defecto; con roles de
/// aplicación de Entra ID y el mapeo de claims activo, se declara
/// <see cref="ClaimTypes.Role"/>.
/// </summary>
public sealed class PermissionPolicyOptions
{
    /// <summary>Claim con los permisos cuando el emisor no impone otro.</summary>
    public const string DefaultClaimType = "permissions";

    public string ClaimType { get; set; } = DefaultClaimType;
}

/// <summary>
/// Nombre de la política que exige un permiso. Se compone en un solo sitio para
/// que el endpoint no repita una cadena que después no coincida con el registro.
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "permission:";

    public static string NameFor(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (permission != permission.Trim())
        {
            throw new ArgumentException(
                "El permiso no puede empezar ni acabar con espacios.",
                nameof(permission));
        }

        return Prefix + permission;
    }
}

/// <summary>
/// Sirve una política por permiso sin que el consumidor tenga que enumerarlos.
/// Un nombre desconocido sigue al proveedor registrado por el consumidor.
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies =
        new(StringComparer.Ordinal);

    private readonly PermissionPolicyOptions _options;

    public PermissionPolicyProvider(
        IOptions<AuthorizationOptions> options,
        IOptions<PermissionPolicyOptions> permissionOptions)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(permissionOptions);
        _options = permissionOptions.Value;
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        if (await base.GetPolicyAsync(policyName) is not null)
        {
            throw new InvalidOperationException(
                $"La política '{policyName}' usa el prefijo reservado '{PermissionPolicy.Prefix}'.");
        }

        var permission = policyName[PermissionPolicy.Prefix.Length..];
        if (string.IsNullOrWhiteSpace(permission))
        {
            return null;
        }

        return _policies.GetOrAdd(
            permission,
            static (name, claimType) => new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(claimType, name)
                .Build(),
            _options.ClaimType);
    }
}

public static class PermissionExtensions
{
    /// <summary>
    /// Registra el proveedor de políticas por permiso. Las políticas que el
    /// consumidor declare en <c>AuthorizationOptions</c> se siguen resolviendo;
    /// el prefijo <c>permission:</c> queda reservado y el registro sustituye al
    /// proveedor de políticas, así que no se combina con otro propio.
    /// </summary>
    public static IServiceCollection AddPermissionPolicies(
        this IServiceCollection services,
        Action<PermissionPolicyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PermissionPolicyOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ClaimType)
            || options.ClaimType != options.ClaimType.Trim())
        {
            throw new ArgumentException(
                "ClaimType no puede estar vacío ni tener espacios en los extremos.",
                nameof(configure));
        }

        services.AddOptions<PermissionPolicyOptions>()
            .Configure(configured => configured.ClaimType = options.ClaimType);
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        return services;
    }

    /// <summary>
    /// Exige el permiso en el endpoint. Sin token responde 401; con un token
    /// válido sin el permiso, 403.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(PermissionPolicy.NameFor(permission));
    }
}
