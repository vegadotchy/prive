using System.Threading.Tasks;
using Windows.Security.Credentials.UI;

namespace IatechShield.Gui;

/// <summary>
/// Authentification biométrique via <b>Windows Hello</b> (reconnaissance faciale,
/// empreinte ou PIN Windows). S'appuie sur l'API système : aucune donnée
/// biométrique n'est manipulée par l'application. Dégrade proprement si Hello
/// n'est pas configuré sur la machine.
/// </summary>
public static class BiometricAuth
{
    /// <summary>Vrai si un mécanisme Windows Hello est disponible et configuré.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync()
                   == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Demande une vérification Windows Hello. Renvoie true si vérifié.</summary>
    public static async Task<bool> VerifyAsync(string message)
    {
        try
        {
            var result = await UserConsentVerifier.RequestVerificationAsync(message);
            return result == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }
}
