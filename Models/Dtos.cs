namespace TrueInjector.Models;

/// <summary>
/// Cached, immutable inspection of the selected payload.
/// </summary>
public sealed record DllInspection(string Path, string Architecture, bool IsLibrary, bool HasEntryPoint, long SizeBytes, DateTime ModifiedUtc)
{
    public string SizeLabel => SizeBytes switch
    {
        < 1024 * 1024 => $"{Math.Round(SizeBytes / 1024.0, 1)} KB",
        < 100L * 1024 * 1024 => $"{Math.Round(SizeBytes / (1024.0 * 1024.0), 1)} MB",
        _ => $"{Math.Round(SizeBytes / (1024.0 * 1024.0))} MB",
    };

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Summary => $"{Architecture} · {(IsLibrary ? "library" : "unknown type")} · {SizeLabel}";

    public bool LooksLikeDll => System.IO.Path.GetExtension(Path).Equals(".dll", StringComparison.OrdinalIgnoreCase);
}

public sealed record ActivityEntry(DateTime Timestamp, ActivityKind Kind, string Message)
{
    public string TimeLabel => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public static ActivityEntry Info(string message) => new(DateTime.UtcNow, ActivityKind.Info, message);

    public static ActivityEntry Success(string message) => new(DateTime.UtcNow, ActivityKind.Success, message);

    public static ActivityEntry Error(string message) => new(DateTime.UtcNow, ActivityKind.Error, message);

    public static ActivityEntry Action(string message) => new(DateTime.UtcNow, ActivityKind.Action, message);
}
