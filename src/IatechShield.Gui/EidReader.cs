using System.Runtime.InteropServices;
using System.Text;

namespace IatechShield.Gui;

/// <summary>Informations lues sur une carte insérée dans un lecteur.</summary>
public sealed record CardInfo
{
    public bool CardPresent { get; init; }
    public string ReaderName { get; init; } = "";
    public string Atr { get; init; } = "";
    public bool IsBelgianEid { get; init; }
    public string Name { get; init; } = "";
    public string FirstNames { get; init; } = "";
    public string NationalNumber { get; init; } = "";
    public string BirthDate { get; init; } = "";
    public string BirthPlace { get; init; } = "";
    public string Nationality { get; init; } = "";

    /// <summary>Identité affichable (nom complet ou repli sur l'ATR).</summary>
    public string DisplayIdentity =>
        IsBelgianEid && !string.IsNullOrWhiteSpace(Name)
            ? $"{FirstNames} {Name}".Trim()
            : (CardPresent ? "Carte inconnue" : "Aucune carte");
}

/// <summary>
/// Lecteur de carte à puce (PC/SC via winscard.dll). Détecte la présence d'une carte
/// dans un lecteur et, s'il s'agit d'une carte d'identité belge (eID), lit le fichier
/// d'identité et en extrait nom, prénoms, numéro national, date/lieu de naissance.
/// Tout échec est géré sans exception (machine sans lecteur, carte étrangère, etc.).
/// </summary>
public static class EidReader
{
    private const uint SCARD_SCOPE_USER = 0;
    private const uint SCARD_SHARE_SHARED = 2;
    private const uint SCARD_PROTOCOL_T0 = 1;
    private const uint SCARD_PROTOCOL_T1 = 2;
    private const uint SCARD_LEAVE_CARD = 0;

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardEstablishContext(uint scope, IntPtr r1, IntPtr r2, out IntPtr ctx);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardReleaseContext(IntPtr ctx);

    [DllImport("winscard.dll", EntryPoint = "SCardListReadersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SCardListReaders(IntPtr ctx, string? groups, char[]? readers, ref uint len);

    [DllImport("winscard.dll", EntryPoint = "SCardConnectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SCardConnect(IntPtr ctx, string reader, uint share, uint protocols,
        out IntPtr card, out uint activeProtocol);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardDisconnect(IntPtr card, uint disposition);

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardStatus(IntPtr card, char[]? reader, ref uint readerLen, out uint state,
        out uint protocol, byte[]? atr, ref uint atrLen);

    [StructLayout(LayoutKind.Sequential)]
    private struct SCARD_IO_REQUEST
    {
        public uint dwProtocol;
        public uint cbPciLength;
    }

    [DllImport("winscard.dll", SetLastError = true)]
    private static extern int SCardTransmit(IntPtr card, ref SCARD_IO_REQUEST sendPci, byte[] sendBuffer,
        uint sendLength, IntPtr recvPci, byte[] recvBuffer, ref uint recvLength);

    /// <summary>Liste les lecteurs de carte branchés.</summary>
    public static List<string> ListReaders()
    {
        var result = new List<string>();
        if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var ctx) != 0)
            return result;
        try
        {
            uint len = 0;
            if (SCardListReaders(ctx, null, null, ref len) != 0 || len == 0) return result;
            var buf = new char[len];
            if (SCardListReaders(ctx, null, buf, ref len) != 0) return result;
            foreach (var r in new string(buf, 0, (int)len).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                result.Add(r);
        }
        finally { SCardReleaseContext(ctx); }
        return result;
    }

    /// <summary>Indique si une carte est actuellement insérée dans un lecteur (test léger).</summary>
    public static bool IsCardPresent()
    {
        if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var ctx) != 0)
            return false;
        try
        {
            foreach (var reader in ListReaders())
            {
                if (SCardConnect(ctx, reader, SCARD_SHARE_SHARED, SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1,
                        out var card, out _) == 0)
                {
                    SCardDisconnect(card, SCARD_LEAVE_CARD);
                    return true;   // au moins un lecteur contient une carte
                }
            }
        }
        finally { SCardReleaseContext(ctx); }
        return false;
    }

    /// <summary>Indique s'il existe au moins un lecteur de carte branché.</summary>
    public static bool HasReader() => ListReaders().Count > 0;

    /// <summary>Tente de lire la première carte présente dans un lecteur.</summary>
    public static CardInfo Read()
    {
        if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var ctx) != 0)
            return new CardInfo();
        try
        {
            foreach (var reader in ListReaders())
            {
                if (SCardConnect(ctx, reader, SCARD_SHARE_SHARED, SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1,
                        out var card, out var proto) != 0)
                    continue;
                try
                {
                    string atr = ReadAtr(card);
                    var io = new SCARD_IO_REQUEST { dwProtocol = proto, cbPciLength = 8 };
                    var id = ReadBelgianIdentity(card, io);
                    if (id is not null)
                        return id with { ReaderName = reader, Atr = atr };
                    return new CardInfo { CardPresent = true, ReaderName = reader, Atr = atr };
                }
                finally { SCardDisconnect(card, SCARD_LEAVE_CARD); }
            }
        }
        finally { SCardReleaseContext(ctx); }
        return new CardInfo();
    }

    private static string ReadAtr(IntPtr card)
    {
        try
        {
            uint rlen = 0, atrLen = 64;
            var atr = new byte[64];
            if (SCardStatus(card, null, ref rlen, out _, out _, atr, ref atrLen) == 0 && atrLen > 0)
                return Convert.ToHexString(atr, 0, (int)atrLen);
        }
        catch { }
        return "";
    }

    /// <summary>Sélectionne et lit le fichier d'identité de la carte d'identité belge.</summary>
    private static CardInfo? ReadBelgianIdentity(IntPtr card, SCARD_IO_REQUEST io)
    {
        try
        {
            // SELECT MF puis DF(BELPIC) puis EF(identité 0x4031), via chemin absolu.
            byte[] selectId = { 0x00, 0xA4, 0x08, 0x0C, 0x06, 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x31 };
            if (!Transmit(card, io, selectId, out _)) return null;

            var data = ReadBinary(card, io);
            if (data.Length == 0) return null;

            var f = ParseTlv(data);
            string Get(int tag) => f.TryGetValue(tag, out var v) ? Encoding.UTF8.GetString(v) : "";

            // Champs du fichier d'identité belge (tags TLV).
            string nationalNumber = Get(6);
            string name = Get(7);
            string firstNames = Get(8);
            string nationality = Get(5);
            string birthPlace = Get(10);
            string birthDate = Get(11);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(nationalNumber))
                return null;   // pas une eID belge exploitable

            return new CardInfo
            {
                CardPresent = true,
                IsBelgianEid = true,
                Name = name,
                FirstNames = firstNames,
                NationalNumber = nationalNumber,
                Nationality = nationality,
                BirthPlace = birthPlace,
                BirthDate = birthDate
            };
        }
        catch { return null; }
    }

    private static byte[] ReadBinary(IntPtr card, SCARD_IO_REQUEST io)
    {
        var all = new List<byte>();
        int offset = 0;
        for (int i = 0; i < 64; i++)   // garde-fou : ≤ 64 blocs de 0xFF octets
        {
            byte[] read = { 0x00, 0xB0, (byte)(offset >> 8), (byte)(offset & 0xFF), 0xFF };
            if (!Transmit(card, io, read, out var resp) || resp.Length == 0) break;
            all.AddRange(resp);
            if (resp.Length < 0xFF) break;
            offset += resp.Length;
        }
        return all.ToArray();
    }

    private static bool Transmit(IntPtr card, SCARD_IO_REQUEST io, byte[] apdu, out byte[] response)
    {
        response = Array.Empty<byte>();
        var recv = new byte[512];
        uint recvLen = (uint)recv.Length;
        int rc = SCardTransmit(card, ref io, apdu, (uint)apdu.Length, IntPtr.Zero, recv, ref recvLen);
        if (rc != 0 || recvLen < 2) return false;
        // Status word 0x9000 = OK ; on retourne les données sans le SW.
        byte sw1 = recv[recvLen - 2], sw2 = recv[recvLen - 1];
        response = recv[..(int)(recvLen - 2)];
        return sw1 == 0x90 && sw2 == 0x00;
    }

    /// <summary>Décode une structure TLV simple (tag 1 octet, longueur 1 octet).</summary>
    private static Dictionary<int, byte[]> ParseTlv(byte[] data)
    {
        var map = new Dictionary<int, byte[]>();
        int i = 0;
        while (i + 1 < data.Length)
        {
            int tag = data[i++];
            int len = data[i++];
            if (len < 0 || i + len > data.Length) break;
            map[tag] = data[i..(i + len)];
            i += len;
        }
        return map;
    }
}
