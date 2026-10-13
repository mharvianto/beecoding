using System.Globalization;
using System.Reflection;

namespace BeeCoding.Services;

/// <summary>Which build is running: the short commit hash the SDK stamps into the assembly's informational version
/// (<c>1.0.0+&lt;sha&gt;</c>) and the UTC time the build ran (the <c>BuildDate</c> assembly metadata set in the csproj).
/// Both are null when the build had no git checkout to read from.</summary>
public static class BuildInfo
{
    public static string? Commit { get; }
    public static string? BuiltAt { get; }

    static BuildInfo()
    {
        var asm = typeof(BuildInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = info?.IndexOf('+') ?? -1;
        if (info is not null && plus >= 0 && info.Length > plus + 1)
            Commit = info[(plus + 1)..][..Math.Min(7, info.Length - plus - 1)];

        var date = asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
            BuiltAt = t.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
