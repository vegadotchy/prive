namespace IatechShield.Gui;

/// <summary>Constantes de marque / boutique (liens directs vers iatechfutur.be).</summary>
public static class Branding
{
    /// <summary>Boutique IATECHFUTUR.</summary>
    public const string ShopUrl = "https://iatechfutur.be";

    /// <summary>Page produit principale (achat « À vie »).</summary>
    public const string ProductUrl = "https://iatechfutur.be/products/iatech-shield-pro-antivirus-nouvelle-generation";

    // Ajout direct au panier AVEC le plan d'abonnement (selling_plan) :
    // le panier et le paiement afficheront « Abonnement · tous les mois / ans ».
    private const string MonthlyCart = "https://iatechfutur.be/cart/56252004008276:1?selling_plan=690981175636";
    private const string YearlyCart = "https://iatechfutur.be/cart/56252004696404:1?selling_plan=690981142868";
    // Variante « À vie » (paiement unique) — ajout direct au panier.
    private const string LifetimeCart = "https://iatechfutur.be/cart/56251035386196:1";

    /// <summary>
    /// Lien « ajout direct au panier » selon la formule :
    /// mensuel / annuel → abonnement (renouvellement auto, mention « Abonnement » au panier) ;
    /// à vie → paiement unique.
    /// </summary>
    public static string ShopUrlForPlan(string plan) => plan switch
    {
        "monthly" => MonthlyCart,
        "yearly" => YearlyCart,
        "lifetime" => LifetimeCart,
        _ => ProductUrl
    };
}
