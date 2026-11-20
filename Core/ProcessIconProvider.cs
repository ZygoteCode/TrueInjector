using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrueInjector.Core;

/// <summary>
/// Extracts the main window/executable icon for running processes.
/// Icons are cached by process image name, deduplicated, frozen and shared
/// across every list row, so scanning hundreds of processes stays cheap.
/// </summary>
public static class ProcessIconProvider
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] KnownFallbacks =
    [
        "%SystemRoot%\\System32\\SHELL32.dll",
        "%SystemRoot%\\System32\\ddores.dll",
    ];

    /// <summary>
    /// Returns the icon for the given process name, or null when unavailable.
    /// Safe to call from any thread; results are frozen for cross-thread binding.
    /// </summary>
    public static ImageSource? GetIcon(string processName)
    {
        if (Cache.TryGetValue(processName, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? resolved = Resolve(processName);
        Cache[processName] = resolved;

        if (resolved is not null)
        {
            resolved.Freeze();
        }

        return resolved;
    }

    private static ImageSource? Resolve(string processName)
    {
        Process[] processes = [];

        try
        {
            processes = Process.GetProcessesByName(processName);

            foreach (Process process in processes)
            {
                string? exePath = SafeExePath(process);

                if (exePath is null)
                {
                    continue;
                }

                ImageSource? icon = ExtractFrom(exePath, 0);

                if (icon is not null)
                {
                    return icon;
                }
            }

            // Some system processes expose no accessible path; fall back to generic shell icons
            // so rows never look broken.
            foreach (string fallback in KnownFallbacks)
            {
                string expanded = Environment.ExpandEnvironmentVariables(fallback);
                ImageSource? icon = ExtractFrom(expanded, 4);

                if (icon is not null)
                {
                    return icon;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string? SafeExePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            // Access-controlled processes simply yield no icon.
            return null;
        }
    }

    private static ImageSource? ExtractFrom(string imagePath, int iconIndex)
    {
        IntPtr[] handles = new IntPtr[1];
        const uint iconSize = 32 | (16u << 16); // MAKELONG(large, small)

        try
        {
            uint extracted = SHDefExtractIcon(imagePath, unchecked((uint)iconIndex), 0, handles, null, iconSize);

            if (extracted != 0 || handles[0] == IntPtr.Zero)
            {
                return null;
            }

            ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                handles[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
            {
                _ = DestroyIcon(handles[0]);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = false)]
    private static extern uint SHDefExtractIcon(string pszIconFile, uint iIndex, uint flags, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIconSize);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = false)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
