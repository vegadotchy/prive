using System.Diagnostics;

namespace IatechShield.Tools;

/// <summary>Caractéristiques matérielles et système du PC.</summary>
public sealed record SystemInfoData(
    string Os, string OsVersion, string InstallDate,
    string Cpu, string CpuCores, string RamTotal,
    string Gpu, string Machine, IReadOnlyList<string> Disks);

/// <summary>
/// Récupère les informations système (Windows, processeur, RAM, carte graphique,
/// disques) via WMI/CIM. Appelé une fois pour l'affichage du tableau de bord.
/// </summary>
public static class SystemInfo
{
    public static async Task<SystemInfoData> GatherAsync()
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "$os=Get-CimInstance Win32_OperatingSystem;" +
            "Write-Output ('os=' + $os.Caption);" +
            "Write-Output ('osver=' + $os.Version + ' (build ' + $os.BuildNumber + ')');" +
            "Write-Output ('install=' + $os.InstallDate.ToString('dd/MM/yyyy'));" +
            "$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1;" +
            "Write-Output ('cpu=' + $cpu.Name.Trim());" +
            "Write-Output ('cores=' + $cpu.NumberOfCores + ' coeurs / ' + $cpu.NumberOfLogicalProcessors + ' threads');" +
            "$cs=Get-CimInstance Win32_ComputerSystem;" +
            "Write-Output ('ram=' + [math]::Round($cs.TotalPhysicalMemory/1GB,1) + ' Go');" +
            "Write-Output ('machine=' + $cs.Manufacturer + ' ' + $cs.Model);" +
            "$gpu=Get-CimInstance Win32_VideoController | Where-Object {$_.Name} | Select-Object -First 1;" +
            "Write-Output ('gpu=' + $gpu.Name);" +
            "foreach($d in (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3')){" +
            "Write-Output ('disk=' + $d.DeviceID + ' ' + [math]::Round($d.Size/1GB) + ' Go (' + [math]::Round($d.FreeSpace/1GB) + ' Go libres)')}";

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var disks = new List<string>();
        try
        {
            string output = await RunPowerShellAsync(script);
            foreach (var line in output.Split('\n'))
            {
                var kv = line.TrimEnd('\r').Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "disk") disks.Add(kv[1].Trim());
                else map[kv[0]] = kv[1].Trim();
            }
        }
        catch { /* infos partielles tolérées */ }

        string Get(string k, string fb = "—") => map.TryGetValue(k, out var v) && v.Length > 0 ? v : fb;
        return new SystemInfoData(
            Get("os", Environment.OSVersion.VersionString),
            Get("osver"), Get("install"),
            Get("cpu"), Get("cores"), Get("ram"),
            Get("gpu"), Get("machine", Environment.MachineName),
            disks);
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
