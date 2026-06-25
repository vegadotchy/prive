namespace IatechShield.Licensing;

/// <summary>État global d'autorisation de l'application.</summary>
public enum LicenseState
{
    TrialActive,
    TrialExpired,
    Licensed,
    Unlicensed
}

/// <summary>Synthèse de l'état de licence, prête à afficher.</summary>
public sealed class LicenseStatus
{
    public LicenseState State { get; init; }
    public int TrialDaysRemaining { get; init; }
    public License? License { get; init; }
    public string Message { get; init; } = "";

    /// <summary>L'application est-elle autorisée à fonctionner pleinement ?</summary>
    public bool IsActivated => State is LicenseState.Licensed or LicenseState.TrialActive;
}

/// <summary>
/// Combine essai gratuit + licence signée pour déterminer si l'application est
/// autorisée. Stocke la clé activée localement.
/// </summary>
public sealed class LicenseManager
{
    private readonly string _keyPath;
    private readonly TrialManager _trial;
    private readonly LicenseVerifier _verifier = new();

    public LicenseManager(string? storageDir = null)
    {
        storageDir ??= DefaultStorageDir();
        Directory.CreateDirectory(storageDir);
        _keyPath = Path.Combine(storageDir, "license.key");
        _trial = new TrialManager(storageDir);
    }

    public static string DefaultStorageDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "IatechShield", "license");
    }

    /// <summary>Évalue l'état courant (licence valide en priorité, sinon essai).</summary>
    public LicenseStatus GetStatus(DateTimeOffset now)
    {
        // 1) Une licence activée et valide prime sur tout.
        if (File.Exists(_keyPath))
        {
            string key = File.ReadAllText(_keyPath);
            var check = _verifier.Verify(key, now);
            if (check.Valid)
            {
                return new LicenseStatus
                {
                    State = LicenseState.Licensed,
                    License = check.License,
                    Message = check.License!.IsLifetime
                        ? "Licence à vie active."
                        : $"Licence {check.License.TierLabel} active jusqu'au {check.License.ExpiresUtc:yyyy-MM-dd}."
                };
            }
            // Licence présente mais invalide/expirée : on retombe sur l'essai.
        }

        // 2) Période d'essai.
        int days = _trial.DaysRemaining(now);
        return days > 0
            ? new LicenseStatus
            {
                State = LicenseState.TrialActive,
                TrialDaysRemaining = days,
                Message = $"Version d'essai — {days} jour(s) restant(s)."
            }
            : new LicenseStatus
            {
                State = LicenseState.TrialExpired,
                TrialDaysRemaining = 0,
                Message = "Période d'essai terminée. Veuillez activer une licence."
            };
    }

    /// <summary>Tente d'activer une clé de licence. Renvoie le résultat de vérification.</summary>
    public LicenseCheck Activate(string licenseKey, DateTimeOffset now)
    {
        var check = _verifier.Verify(licenseKey, now);
        if (check.Valid)
            File.WriteAllText(_keyPath, licenseKey.Trim());
        return check;
    }

    /// <summary>Supprime la licence activée (revient en mode essai).</summary>
    public void Deactivate()
    {
        if (File.Exists(_keyPath))
            File.Delete(_keyPath);
    }
}
