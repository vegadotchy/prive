namespace IatechShield.Licensing;

/// <summary>Une formule tarifaire proposée à l'utilisateur.</summary>
public sealed record Plan(LicenseTier Tier, string Name, decimal Price, string Period, string? Note = null);

/// <summary>Grille tarifaire d'IATECH-SHIELD PRO.</summary>
public static class Pricing
{
    public const int TrialDays = TrialManager.TrialDays; // 15 jours

    public static readonly IReadOnlyList<Plan> Plans = new[]
    {
        new Plan(LicenseTier.Monthly,  "Mensuel", 5.00m,   "par mois"),
        new Plan(LicenseTier.Yearly,   "Annuel",  50.00m,  "par an", "2 mois offerts"),
        new Plan(LicenseTier.Lifetime, "À vie",   249.99m, "paiement unique")
    };
}
