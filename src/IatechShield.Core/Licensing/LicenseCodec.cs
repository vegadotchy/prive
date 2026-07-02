using System.Buffers.Binary;
using System.Text;

namespace IatechShield.Licensing;

/// <summary>
/// Encode/décode une licence sous forme binaire compacte, puis en chaîne Base32
/// lisible et groupée (ex: ABCDE-FGHIJ-...). Le format est :
///
///   [payload 34 octets] + [signature 64 octets]  → 98 octets → Base32
///
/// Payload :
///   octet  0      : version du format (= 1)
///   octet  1      : tier (0..3)
///   octets 2..9   : date d'émission   (Int64, secondes Unix UTC)
///   octets 10..17 : date d'expiration (Int64, 0 = à vie)
///   octets 18..33 : identifiant GUID (16 octets)
/// </summary>
public static class LicenseCodec
{
    public const byte FormatVersion = 1;
    public const int PayloadLength = 34;
    public const int SignatureLength = 64;

    /// <summary>Sérialise la licence en payload binaire (sans signature).</summary>
    public static byte[] BuildPayload(License license)
    {
        var buf = new byte[PayloadLength];
        buf[0] = FormatVersion;
        buf[1] = (byte)license.Tier;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(2, 8), license.IssuedUtc.ToUnixTimeSeconds());
        long exp = license.ExpiresUtc?.ToUnixTimeSeconds() ?? 0;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(10, 8), exp);
        license.LicenseId.TryWriteBytes(buf.AsSpan(18, 16));
        return buf;
    }

    /// <summary>Reconstruit une licence depuis un payload binaire.</summary>
    public static License ParsePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != PayloadLength)
            throw new FormatException("Longueur de payload de licence invalide.");
        if (payload[0] != FormatVersion)
            throw new FormatException($"Version de licence non supportée : {payload[0]}.");

        long issued = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(2, 8));
        long exp = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(10, 8));

        return new License
        {
            Tier = (LicenseTier)payload[1],
            IssuedUtc = DateTimeOffset.FromUnixTimeSeconds(issued),
            ExpiresUtc = exp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(exp),
            LicenseId = new Guid(payload.Slice(18, 16))
        };
    }

    /// <summary>Assemble payload + signature et renvoie la clé Base32 groupée.</summary>
    public static string Encode(byte[] payload, byte[] signature)
    {
        if (signature.Length != SignatureLength)
            throw new ArgumentException("Signature de taille inattendue.", nameof(signature));

        var all = new byte[payload.Length + signature.Length];
        Buffer.BlockCopy(payload, 0, all, 0, payload.Length);
        Buffer.BlockCopy(signature, 0, all, payload.Length, signature.Length);
        return Group(Base32Encode(all));
    }

    /// <summary>Décode une clé en (payload, signature). Lève une exception si malformée.</summary>
    public static (byte[] payload, byte[] signature) Decode(string key)
    {
        byte[] all = Base32Decode(Ungroup(key));
        if (all.Length != PayloadLength + SignatureLength)
            throw new FormatException("Clé de licence de longueur invalide.");

        var payload = new byte[PayloadLength];
        var signature = new byte[SignatureLength];
        Buffer.BlockCopy(all, 0, payload, 0, PayloadLength);
        Buffer.BlockCopy(all, PayloadLength, signature, 0, SignatureLength);
        return (payload, signature);
    }

    // ----------------------------- Base32 (RFC 4648, sans remplissage) --------

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string s)
    {
        var bytes = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (char c in s)
        {
            int val = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (val < 0)
                throw new FormatException($"Caractère invalide dans la clé : '{c}'.");
            buffer = (buffer << 5) | val;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }

    private static string Group(string s)
    {
        var sb = new StringBuilder(s.Length + s.Length / 5);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
                sb.Append('-');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string Ungroup(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}
