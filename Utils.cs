using PeNet;
using PeNet.Header.Pe;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class Utils
{
    [DllImport("kernel32.dll")]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    public static bool CanBeInjected(ListView listView, string dllPath)
    {
        try
        {
            if (listView.SelectedItems.Count == 0)
            {
                return false;
            }

            if (!File.Exists(dllPath))
            {
                return false;
            }

            if (!Path.GetExtension(dllPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var item = listView.SelectedItems[0];

            if (item == null)
            {
                return false;
            }

            var process = Process.GetProcessById(int.Parse(item.Text));

            if (process == null)
            {
                return false;
            }

            var pe = new PeFile(dllPath);

            if (pe.ImageNtHeaders.OptionalHeader.AddressOfEntryPoint == 0)
            {
                return false;
            }

            var dllArch = pe.ImageNtHeaders.FileHeader.Machine;

            bool processIs64 = Environment.Is64BitOperatingSystem &&
                               IsProcess64Bit(process);

            if (dllArch == MachineType.Amd64 && !processIs64)
            {
                return false;
            }

            if (dllArch == MachineType.I386 && processIs64)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcess64Bit(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        bool isWow64;
        IsWow64Process(process.Handle, out isWow64);
        return !isWow64;
    }
}