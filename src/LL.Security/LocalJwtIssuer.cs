using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LL.Security;

/// <summary>
/// Emisor de tokens para pruebas y desarrollo local. Firma con una clave
/// simétrica aleatoria que vive en memoria: solo la valida el pipeline montado
/// con <see cref="JwtBearerExtensions.AddLocalJwtBearer"/> sobre esta instancia.
/// No contacta con ningún tenant ni sustituye a la validación de Entra ID.
/// </summary>
public sealed class LocalJwtIssuer
{
    public const string DefaultIssuer = "https://local.test/";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    public LocalJwtIssuer(string audience, string issuer = DefaultIssuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        EnsureAbsoluteIssuer(issuer, nameof(issuer));

        Audience = audience;
        Issuer = issuer;
        SigningKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
    }

    public string Audience { get; }

    public string Issuer { get; }

    /// <summary>Clave de firma de esta instancia. Nunca se escribe en disco.</summary>
    public SymmetricSecurityKey SigningKey { get; }

    /// <summary>
    /// Emite un token válido contra el pipeline local. Una vigencia negativa
    /// produce un token ya vencido, para probar que se rechaza. El emisor y la
    /// audiencia se pueden sustituir para probar los rechazos por identidad.
    /// </summary>
    public string CreateToken(
        string subject = "local-user",
        IEnumerable<Claim>? claims = null,
        TimeSpan? lifetime = null,
        string? issuer = null,
        string? audience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        EnsureAbsoluteIssuer(issuer ?? Issuer, nameof(issuer));
        ArgumentException.ThrowIfNullOrWhiteSpace(audience ?? Audience);

        var now = DateTime.UtcNow;
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, subject));

        foreach (var claim in claims ?? [])
        {
            identity.AddClaim(claim);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime ?? DefaultLifetime),
            Subject = identity,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Emite un token con los permisos indicados, que es lo que exige
    /// <c>RequirePermission</c>.
    /// </summary>
    public string CreateTokenWithPermissions(
        IEnumerable<string> permissions,
        string subject = "local-user",
        TimeSpan? lifetime = null,
        string claimType = PermissionPolicyOptions.DefaultClaimType,
        string? issuer = null,
        string? audience = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return CreateToken(
            subject,
            permissions.Select(permission => new Claim(claimType, permission)),
            lifetime,
            issuer,
            audience);
    }

    private static void EnsureAbsoluteIssuer(string issuer, string parameterName)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "Issuer debe ser una URL absoluta.",
                parameterName);
        }
    }
}
