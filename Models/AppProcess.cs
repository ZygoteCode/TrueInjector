using System.Windows.Media;

namespace TrueInjector.Models;

public sealed class AppProcess
{
    private static readonly IReadOnlyDictionary<string, int> KnownHues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = 205,
        ["chrome"] = 15,
        ["firefox"] = 25,
        ["edge"] = 200,
        ["code"] = 210,
        ["devenv"] = 250,
        ["notepad"] = 95,
        ["notepad++"] = 95,
        ["svchost"] = 265,
        ["winlogon"] = 280,
        ["steam"] = 215,
        ["discord"] = 235,
        ["spotify"] = 140,
        ["cmd"] = 0,
        ["powershell"] = 260,
        ["windowsterminal"] = 260,
        ["taskmgr"] = 175,
        ["outlook"] = 10,
        ["excel"] = 150,
        ["winword"] = 215,
    };

    public AppProcess(uint processId, string processName)
    {
        ProcessId = processId;
        ProcessName = processName;
        Initials = DeriveInitials(processName);

        SolidColorBrush brush = new(DeriveAccent(processName, processId));
        brush.Freeze();
        AvatarBrush = brush;

        SolidColorBrush brushDim = new(System.Windows.Media.Color.FromArgb(28, AvatarBrush.Color.R, AvatarBrush.Color.G, AvatarBrush.Color.B));
        brushDim.Freeze();
        AvatarBrushDim = brushDim;
    }

    private ImageSource? _icon;
    private bool _iconResolved;

    public uint ProcessId { get; }

    public string ProcessName { get; }

    public string Initials { get; }

    public SolidColorBrush AvatarBrush { get; }

    public SolidColorBrush AvatarBrushDim { get; }

    /// <summary>
    /// Lazily resolved on first binding access: scanning never touches disk,
    /// virtualised rows pay a cached lookup only when they become visible.
    /// </summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconResolved)
            {
                _iconResolved = true;
                _icon = Core.ProcessIconProvider.GetIcon(ProcessName);
            }

            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    public string DisplayId => ProcessId.ToString("D");

    private static string DeriveInitials(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "··";
        }

        string[] parts = trimmed.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2 && char.IsLetter(parts[0][0]) && char.IsLetter(parts[1][0]))
        {
            return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
        }

        return new string(trimmed.Where(char.IsLetterOrDigit).Take(2).Select(char.ToUpperInvariant).ToArray()).PadRight(2, '·');
    }

    private static System.Windows.Media.Color DeriveAccent(string name, uint pid)
    {
        if (KnownHues.TryGetValue(name, out int hue))
        {
            return FromHsv(hue % 360, 0.62, 0.72);
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }

            uint mixed = hash ^ (pid * 2654435761u);
            return FromHsv(mixed % 360, 0.55, 0.70);
        }
    }

    private static System.Windows.Media.Color FromHsv(double degrees, double saturation, double value)
    {
        double chroma = value * saturation;
        double huePrime = degrees / 60.0;
        double x = chroma * (1 - Math.Abs(huePrime % 2 - 1));
        double m = value - chroma;

        double r;
        double g;
        double b;

        if (huePrime < 1)
        {
            r = chroma;
            g = x;
            b = 0;
        }
        else if (huePrime < 2)
        {
            r = x;
            g = chroma;
            b = 0;
        }
        else if (huePrime < 3)
        {
            r = 0;
            g = chroma;
            b = x;
        }
        else if (huePrime < 4)
        {
            r = 0;
            g = x;
            b = chroma;
        }
        else if (huePrime < 5)
        {
            r = x;
            g = 0;
            b = chroma;
        }
        else
        {
            r = chroma;
            g = 0;
            b = x;
        }

        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
