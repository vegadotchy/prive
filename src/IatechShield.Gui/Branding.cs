namespace IatechShield.Gui;

/// <summary>Constantes de marque / boutique. Modifiez ici le lien direct.</summary>
public static class Branding
{
    /// <summary>Boutique IATECHFUTUR — remplacez par le lien direct définitif.</summary>
    public const string ShopUrl = "https://iatechfutur.be";

    public static string ShopUrlForPlan(string plan) => $"{ShopUrl}/?produit=iatech-shield&offre={plan}";
}
