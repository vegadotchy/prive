namespace IatechShield.Licensing;

/// <summary>Type de licence.</summary>
public enum LicenseTier : byte
{
    Trial = 0,
    Monthly = 1,
    Yearly = 2,
    Lifetime = 3
}

/// <summary>
/// Contenu d'une licence. Encodé de façon compacte puis signé.
/// </summary>
public sealed class License
{
    public LicenseTier Tier { get; init; }
    public DateTimeOffset IssuedUtc { get; init; }

    /// <summary>Date d'expiration. Null = à vie.</summary>
    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>Identifiant unique de la licence (anti-rejeu / suivi).</summary>
    public Guid LicenseId { get; init; }

    public bool IsLifetime => Tier == LicenseTier.Lifetime || ExpiresUtc is null;

    public bool IsExpired(DateTimeOffset now) => !IsLifetime && now > ExpiresUtc;

    /// <summary>Crée une licence pour une durée donnée à partir de maintenant.</summary>
    public static License Create(LicenseTier tier, DateTimeOffset now)
    {
        DateTimeOffset? expires = tier switch
        {
            LicenseTier.Monthly => now.AddMonths(1),
            LicenseTier.Yearly => now.AddYears(1),
            LicenseTier.Lifetime => null,
            _ => now.AddDays(15)
        };

        return new License
        {
            Tier = tier,
            IssuedUtc = now,
            ExpiresUtc = expires,
            LicenseId = Guid.NewGuid()
        };
    }

    public string TierLabel => Tier switch
    {
        LicenseTier.Monthly => "Mensuelle",
        LicenseTier.Yearly => "Annuelle",
        LicenseTier.Lifetime => "À vie",
        _ => "Essai"
    };
}
