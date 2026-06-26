namespace IatechShield.Gui;

/// <summary>Constantes de marque / boutique (liens directs vers iatechfutur.be).</summary>
public static class Branding
{
    /// <summary>Boutique IATECHFUTUR.</summary>
    public const string ShopUrl = "https://iatechfutur.be";

    /// <summary>Page produit IATECH-SHIELD PRO.</summary>
    public const string ProductUrl = "https://iatechfutur.be/products/iatech-shield-pro-antivirus-nouvelle-generation";

    // Identifiants de variantes Shopify (ajout direct au panier via /cart/<id>:1).
    private const string VariantMonthly = "56251035320660";
    private const string VariantYearly = "56251035353428";
    private const string VariantLifetime = "56251035386196";

    /// <summary>
    /// Lien « ajout direct au panier » de la boutique iatechfutur.be selon la formule
    /// choisie (mensuel / annuel / à vie). Clic = produit mis dans le panier Shopify.
    /// </summary>
    public static string ShopUrlForPlan(string plan)
    {
        string variant = plan switch
        {
            "monthly" => VariantMonthly,
            "yearly" => VariantYearly,
            "lifetime" => VariantLifetime,
            _ => ""
        };
        return string.IsNullOrEmpty(variant)
            ? ProductUrl
            : $"{ShopUrl}/cart/{variant}:1";
    }
}
