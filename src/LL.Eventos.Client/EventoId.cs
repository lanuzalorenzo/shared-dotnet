using System.Security.Cryptography;
using System.Text;

namespace LL.Eventos.Client;

/// <summary>
/// Identificador de evento del contrato v1: `evt_` y 26 caracteres ULID en
/// Crockford, 48 bits de tiempo y 80 aleatorios. Es la clave de idempotencia
/// del alta: reintentar con el mismo identificador no crea otro evento.
/// </summary>
public static class EventoId
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Prefijo = "evt_";
    private const int LongitudCuerpo = 26;

    /// <summary>Genera un identificador nuevo para el instante indicado.</summary>
    public static string Nuevo(DateTimeOffset? momento = null)
    {
        Span<byte> bytes = stackalloc byte[16];
        var milisegundos = (ulong)(momento ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        bytes[0] = (byte)(milisegundos >> 40);
        bytes[1] = (byte)(milisegundos >> 32);
        bytes[2] = (byte)(milisegundos >> 24);
        bytes[3] = (byte)(milisegundos >> 16);
        bytes[4] = (byte)(milisegundos >> 8);
        bytes[5] = (byte)milisegundos;
        RandomNumberGenerator.Fill(bytes[6..]);

        return Prefijo + Codificar(bytes);
    }

    /// <summary>Comprueba la forma del identificador sin consultar la API.</summary>
    public static bool EsValido(string? id)
    {
        if (id is null
            || id.Length != Prefijo.Length + LongitudCuerpo
            || !id.StartsWith(Prefijo, StringComparison.Ordinal))
        {
            return false;
        }

        var cuerpo = id.AsSpan(Prefijo.Length);

        // El tiempo son 48 bits: el primer carácter solo llega a 7.
        if (cuerpo[0] is < '0' or > '7')
        {
            return false;
        }

        foreach (var caracter in cuerpo)
        {
            if (Crockford.IndexOf(caracter) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Codificar(ReadOnlySpan<byte> datos)
    {
        var texto = new StringBuilder(LongitudCuerpo);
        var buffer = 0;
        // ULID son 128 bits en 130: dos de relleno delante para que el tiempo
        // de 48 bits empiece en el primer carácter y el último no se desplace.
        var bits = 2;

        foreach (var dato in datos)
        {
            buffer = (buffer << 8) | dato;
            bits += 8;
            while (bits >= 5)
            {
                texto.Append(Crockford[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            texto.Append(Crockford[(buffer << (5 - bits)) & 31]);
        }

        return texto.ToString();
    }
}
