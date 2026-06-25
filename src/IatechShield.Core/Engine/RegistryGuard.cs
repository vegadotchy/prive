using Microsoft.Win32;

namespace IatechShield.Engine;

/// <summary>Une entrée de démarrage automatique trouvée dans le registre.</summary>
public sealed record AutoRunEntry(string Location, string Name, string Command, bool Suspicious);

/// <summary>
/// Inspection et contrôle (défensif) du registre Windows. Sert à :
///  - lister les programmes lancés automatiquement au démarrage (zone de
///    prédilection des malwares pour la persistance) ;
///  - signaler les entrées suspectes (emplacements temporaires) ;
///  - inscrire/retirer IATECH-SHIELD du démarrage automatique.
///
/// Toutes les opérations sont limitées à Windows et nécessitent en général des
/// droits administrateur pour la ruche HKLM.
/// </summary>
public sealed class RegistryGuard
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string SelfValueName = "IatechShieldPro";

    private static readonly string[] SuspiciousMarkers =
    {
        @"\temp\", @"\appdata\local\temp", @"\downloads\", @"\programdata\", "%temp%"
    };

    /// <summary>Liste les entrées de démarrage automatique (HKCU + HKLM).</summary>
    public IReadOnlyList<AutoRunEntry> ListAutoRuns()
    {
        var entries = new List<AutoRunEntry>();
        if (!OperatingSystem.IsWindows())
            return entries;

        ReadRunKey(Registry.CurrentUser, "HKCU\\Run", entries);
        ReadRunKey(Registry.LocalMachine, "HKLM\\Run", entries);
        return entries;
    }

    private static void ReadRunKey(RegistryKey hive, string label, List<AutoRunEntry> into)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(RunPath, writable: false);
            if (key is null)
                return;

            foreach (string name in key.GetValueNames())
            {
                string command = key.GetValue(name)?.ToString() ?? "";
                bool suspicious = SuspiciousMarkers.Any(m =>
                    command.Contains(m, StringComparison.OrdinalIgnoreCase));
                into.Add(new AutoRunEntry(label, name, command, suspicious));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // HKLM en lecture peut nécessiter l'élévation : on ignore proprement.
        }
    }

    /// <summary>Inscrit IATECH-SHIELD au démarrage de l'utilisateur courant.</summary>
    public bool EnableSelfAutostart(string exePath)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath);
            key.SetValue(SelfValueName, $"\"{exePath}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Retire IATECH-SHIELD du démarrage automatique.</summary>
    public bool DisableSelfAutostart()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
            key?.DeleteValue(SelfValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
