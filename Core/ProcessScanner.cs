using System.Diagnostics;
using System.Runtime.InteropServices;
using TrueInjector.Models;

namespace TrueInjector.Core;

/// <summary>
/// Enumerates running processes and answers liveness/architecture questions.
/// </summary>
public static class ProcessScanner
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static List<AppProcess> Scan()
    {
        List<AppProcess> processes = [];

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                processes.Add(new AppProcess((uint)process.Id, process.ProcessName));
            }
            catch
            {
                // System processes and access-controlled entries are skipped, as before.
            }
            finally
            {
                process.Dispose();
            }
        }

        processes.Sort(static (a, b) =>
        {
            int byName = string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.ProcessId.CompareTo(b.ProcessId);
        });

        return processes;
    }

    public static bool IsAlive(uint processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, (int)processId);

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        _ = CloseHandle(handle);
        return true;
    }

    public static bool IsProcess64Bit(uint processId)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, (int)processId);

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return !IsWow64Process(handle, out bool wow64) || !wow64;
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }
}
