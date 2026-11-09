namespace LL.Security;

/// <summary>
/// Política de seguridad de contenido (CSP) compuesta por directivas.
/// El consumidor elige las directivas; el paquete valida y serializa.
/// </summary>
public sealed class ContentSecurityPolicy
{
    private readonly List<KeyValuePair<string, string>> _directives = [];

    /// <summary>
    /// Emite la cabecera de informe en lugar de la que bloquea.
    /// Sirve para observar una política antes de aplicarla.
    /// </summary>
    public bool ReportOnly { get; set; }

    /// <summary>
    /// Directivas en orden de inserción, tal como se van a serializar.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Directives => _directives;

    /// <summary>
    /// Verdadero cuando no hay ninguna directiva.
    /// </summary>
    public bool IsEmpty => _directives.Count == 0;

    /// <summary>
    /// Política de una API que solo devuelve datos: nada se carga, nada se
    /// incrusta y no se envía un formulario.
    /// </summary>
    public static ContentSecurityPolicy DenyAll() =>
        new ContentSecurityPolicy()
            .Add("default-src", "'none'")
            .Add("frame-ancestors", "'none'")
            .Add("base-uri", "'none'")
            .Add("form-action", "'none'");

    /// <summary>
    /// Añade una directiva con sus valores. Si la directiva ya existe, la
    /// sustituye: la última definición manda. Una directiva sin valores
    /// —`upgrade-insecure-requests`, `sandbox`— se añade sin argumentos.
    /// </summary>
    public ContentSecurityPolicy Add(string directive, params string[] values)
    {
        ValidateDirective(directive);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            ValidateValue(directive, value);
        }

        var entry = new KeyValuePair<string, string>(directive, string.Join(' ', values));
        var index = _directives.FindIndex(
            existing => string.Equals(existing.Key, directive, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _directives[index] = entry;
        }
        else
        {
            _directives.Add(entry);
        }

        return this;
    }

    /// <summary>
    /// Quita una directiva. No falla si no estaba.
    /// </summary>
    public ContentSecurityPolicy Remove(string directive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directive);
        _directives.RemoveAll(
            existing => string.Equals(existing.Key, directive, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    /// <summary>
    /// Valor de la cabecera: directivas separadas por `; `.
    /// </summary>
    public string ToHeaderValue() =>
        string.Join(
            "; ",
            _directives.Select(entry =>
                string.IsNullOrEmpty(entry.Value) ? entry.Key : $"{entry.Key} {entry.Value}"));

    private static void ValidateDirective(string directive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directive);

        foreach (var character in directive)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                throw new ArgumentException(
                    $"La directiva '{directive}' solo admite letras, dígitos y guiones.",
                    nameof(directive));
            }
        }
    }

    private static void ValidateValue(string directive, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Contains(';', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El valor de '{directive}' no puede contener ';' ni un salto de línea.",
                nameof(value));
        }
    }
}
