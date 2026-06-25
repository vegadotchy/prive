using System.Text;

namespace IatechShield.Engine;

/// <summary>Type d'une chaîne recherchée dans une règle.</summary>
public enum YaraStringKind { Text, Hex }

/// <summary>Une chaîne nommée d'une règle (ex: $a = "CreateRemoteThread").</summary>
public sealed record YaraString(string Id, YaraStringKind Kind, byte[] Pattern);

/// <summary>Mode de condition : au moins une chaîne, ou toutes les chaînes.</summary>
public enum YaraCondition { Any, All }

/// <summary>
/// Règle façon YARA : un nom, un ensemble de chaînes (texte ou hexadécimal) et
/// une condition (« any of them » / « all of them »).
/// </summary>
public sealed class YaraRule
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Severity { get; init; } = "medium";
    public YaraCondition Condition { get; init; } = YaraCondition.Any;
    public List<YaraString> Strings { get; init; } = new();

    /// <summary>Au moins ce nombre de chaînes doit correspondre (pour Condition.Any avec seuil).</summary>
    public int MinMatches { get; init; } = 1;
}

/// <summary>
/// Moteur de règles « façon YARA » : recherche des motifs (texte/hex) dans le
/// contenu d'un fichier selon une condition logique. Volontairement simple et
/// sans dépendance : couvre les cas courants sans embarquer le moteur YARA natif.
/// </summary>
public sealed class YaraEngine
{
    private readonly List<YaraRule> _rules;

    public YaraEngine(IEnumerable<YaraRule> rules) => _rules = rules.ToList();

    public IReadOnlyList<YaraRule> Rules => _rules;

    /// <summary>Renvoie la première règle qui correspond au contenu, ou null.</summary>
    public YaraRule? Match(byte[] content)
    {
        foreach (var rule in _rules)
        {
            int matched = 0;
            foreach (var s in rule.Strings)
            {
                if (IndexOf(content, s.Pattern) >= 0)
                {
                    matched++;
                    if (rule.Condition == YaraCondition.Any && matched >= rule.MinMatches)
                        return rule;
                }
                else if (rule.Condition == YaraCondition.All)
                {
                    matched = -1; // une chaîne manque : la règle « all » échoue
                    break;
                }
            }

            if (rule.Condition == YaraCondition.All && matched == rule.Strings.Count && rule.Strings.Count > 0)
                return rule;
        }
        return null;
    }

    /// <summary>Jeu de règles intégré, couvrant quelques familles/techniques courantes.</summary>
    public static YaraEngine BuiltIn() => new(new[]
    {
        new YaraRule
        {
            Name = "Suspicious_Process_Injection",
            Description = "API d'injection de code dans un autre processus",
            Severity = "high",
            Condition = YaraCondition.Any,
            MinMatches = 2,
            Strings =
            {
                Text("a", "VirtualAllocEx"),
                Text("b", "WriteProcessMemory"),
                Text("c", "CreateRemoteThread"),
                Text("d", "SetWindowsHookEx"),
            }
        },
        new YaraRule
        {
            Name = "Suspicious_Shell_Download_Exec",
            Description = "Téléchargement puis exécution (downloader)",
            Severity = "high",
            Condition = YaraCondition.Any,
            MinMatches = 2,
            Strings =
            {
                Text("a", "URLDownloadToFile"),
                Text("b", "WinExec"),
                Text("c", "ShellExecute"),
                Text("d", "powershell -enc"),
                Text("e", "DownloadString"),
            }
        },
        new YaraRule
        {
            Name = "Suspicious_Keylogger_API",
            Description = "Capture des frappes clavier",
            Severity = "medium",
            Condition = YaraCondition.Any,
            MinMatches = 2,
            Strings =
            {
                Text("a", "GetAsyncKeyState"),
                Text("b", "GetForegroundWindow"),
                Text("c", "SetWindowsHookExA"),
                Text("d", "GetKeyboardState"),
            }
        },
        new YaraRule
        {
            Name = "Mimikatz_Indicators",
            Description = "Marqueurs de l'outil de vol d'identifiants Mimikatz",
            Severity = "high",
            Condition = YaraCondition.Any,
            Strings =
            {
                Text("a", "sekurlsa::logonpasswords"),
                Text("b", "gentilkiwi"),
                Text("c", "mimikatz"),
            }
        },
    });

    private static YaraString Text(string id, string s) =>
        new(id, YaraStringKind.Text, Encoding.ASCII.GetBytes(s));

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return -1;
        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
