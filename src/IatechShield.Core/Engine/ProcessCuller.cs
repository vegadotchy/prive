using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IatechShield.Engine;

/// <summary>Processus suspecté d'activité ransomware.</summary>
public sealed record CulpritProcess(int Pid, string Name, ulong BytesWritten);

/// <summary>
/// Identifie — au mieux, en mode utilisateur — le processus responsable d'une
/// activité disque massive, et peut le terminer.
///
/// Limite assumée : sans pilote noyau, on ne peut pas attribuer une écriture
/// fichier à un PID de façon certaine. On approxime via les compteurs d'E/S du
/// noyau (GetProcessIoCounters) en mesurant le débit d'écriture sur un court
/// intervalle, ce qui suffit pour cibler un ransomware actif. Une attribution
/// fiable viendra avec le module noyau (voir ARCHITECTURE.md).
/// </summary>
public sealed class ProcessCuller
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "wininit", "winlogon", "services",
        "lsass", "smss", "explorer", "svchost", "dwm", "iatech-shield",
        "iatech-shield-gui"
    };

    /// <summary>
    /// Trouve le processus utilisateur écrivant le plus sur le disque sur un
    /// court intervalle. Renvoie null si rien de probant (ou hors Windows).
    /// </summary>
    public CulpritProcess? FindTopWriter(int sampleMs = 120)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // 1er échantillon : octets écrits cumulés par PID.
        var first = SnapshotWrites();
        Thread.Sleep(sampleMs);
        var second = SnapshotWrites();

        CulpritProcess? best = null;
        foreach (var (pid, after) in second)
        {
            if (!first.TryGetValue(pid, out var before))
                continue;
            ulong delta = after.bytes >= before.bytes ? after.bytes - before.bytes : 0;
            if (best is null || delta > best.BytesWritten)
                best = new CulpritProcess(pid, after.name, delta);
        }

        // On n'agit que si l'écriture est réellement significative (> ~1 Mo).
        return best is { BytesWritten: > 1_000_000 } ? best : null;
    }

    private static Dictionary<int, (string name, ulong bytes)> SnapshotWrites()
    {
        var map = new Dictionary<int, (string, ulong)>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (Protected.Contains(proc.ProcessName))
                    continue;
                if (GetProcessIoCounters(proc.Handle, out var io))
                    map[proc.Id] = (proc.ProcessName, io.WriteTransferCount);
            }
            catch
            {
                // Accès refusé (processus protégé) : on ignore.
            }
            finally
            {
                proc.Dispose();
            }
        }
        return map;
    }

    /// <summary>Termine un processus par son PID. Renvoie true si réussi.</summary>
    public bool Kill(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (Protected.Contains(proc.ProcessName))
                return false;
            proc.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);
}
