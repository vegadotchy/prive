using System.Diagnostics;

namespace IatechShield.Tools;

/// <summary>Un accès d'une application à la webcam ou au microphone.</summary>
public sealed record MediaAccess(string Device, string App, bool InUse, string LastUsed);

/// <summary>
/// Surveille les accès à la webcam et au microphone via le registre Windows
/// (CapabilityAccessManager\ConsentStore). Signale les applications qui les
/// utilisent actuellement (LastUsedTimeStop = 0) ou récemment — utile pour
/// repérer un espionnage inhabituel.
/// </summary>
public static class MediaAccessMonitor
{
    public static async Task<IReadOnlyList<MediaAccess>> ScanAsync()
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "$bases=@('HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore'," +
            "'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore');" +
            "foreach($base in $bases){foreach($dev in @('webcam','microphone')){" +
            "$roots=@(\"$base\\$dev\\NonPackaged\", \"$base\\$dev\");" +
            "foreach($root in $roots){" +
            "Get-ChildItem $root | ForEach-Object {" +
            "$p=Get-ItemProperty $_.PSPath;" +
            "if($p.LastUsedTimeStop -ne $null){" +
            "$app=($_.PSChildName -replace '#','\\');" +
            "$inuse= if($p.LastUsedTimeStop -eq 0){'1'}else{'0'};" +
            "Write-Output (\"$dev`t$app`t$inuse\")}}}}};";

        var result = new List<MediaAccess>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string output = await RunPowerShellAsync(script);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length != 3) continue;
                string device = parts[0] == "webcam" ? "Webcam" : "Microphone";
                string app = ShortenApp(parts[1]);
                bool inUse = parts[2] == "1";
                if (seen.Add(device + app))
                    result.Add(new MediaAccess(device, app, inUse, inUse ? "en cours d'utilisation" : "déjà utilisé"));
            }
        }
        catch { /* registre indisponible */ }

        // Les accès en cours d'abord.
        return result.OrderByDescending(a => a.InUse).ThenBy(a => a.Device).ToList();
    }

    private static string ShortenApp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(application inconnue)";
        int slash = raw.LastIndexOf('\\');
        return slash >= 0 && slash < raw.Length - 1 ? raw[(slash + 1)..] : raw;
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
