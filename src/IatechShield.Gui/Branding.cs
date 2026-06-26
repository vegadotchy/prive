namespace IatechShield.Gui;

/// <summary>Constantes de marque / boutique (liens directs vers iatechfutur.be).</summary>
public static class Branding
{
    /// <summary>Boutique IATECHFUTUR.</summary>
    public const string ShopUrl = "https://iatechfutur.be";

    /// <summary>Page produit principale (achat « À vie »).</summary>
    public const string ProductUrl = "https://iatechfutur.be/products/iatech-shield-pro-antivirus-nouvelle-generation";

    // Pages d'abonnement (le client choisit « s'abonner » → prélèvement automatique).
    private const string MonthlyUrl = "https://iatechfutur.be/products/iatech-shield-pro-abonnement-mensuel";
    private const string YearlyUrl = "https://iatechfutur.be/products/iatech-shield-pro-abonnement-annuel";
    // Variante « À vie » (paiement unique) — ajout direct au panier.
    private const string LifetimeCart = "https://iatechfutur.be/cart/56251035386196:1";

    /// <summary>
    /// Lien boutique selon la formule choisie :
    /// mensuel / annuel → page d'abonnement (renouvellement auto) ;
    /// à vie → ajout direct au panier (paiement unique).
    /// </summary>
    public static string ShopUrlForPlan(string plan) => plan switch
    {
        "monthly" => MonthlyUrl,
        "yearly" => YearlyUrl,
        "lifetime" => LifetimeCart,
        _ => ProductUrl
    };
}
