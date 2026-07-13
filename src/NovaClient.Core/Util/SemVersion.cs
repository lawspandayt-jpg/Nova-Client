namespace NovaClient.Core.Util;

/// <summary>Minimal semantic version ("1.2.3" with optional "-prerelease") for update checks.</summary>
public sealed class SemVersion : IComparable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string PreRelease { get; }

    private SemVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major; Minor = minor; Patch = patch; PreRelease = preRelease;
    }

    public static bool TryParse(string? text, out SemVersion version)
    {
        version = new SemVersion(0, 0, 0, "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-');
        var pre = dash >= 0 ? text[(dash + 1)..] : "";
        var core = dash >= 0 ? text[..dash] : text;
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        if (!int.TryParse(parts[0], out var maj) || !int.TryParse(parts[1], out var min)) return false;
        var pat = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out pat)) return false;
        version = new SemVersion(maj, min, pat, pre);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // A pre-release sorts below its release ("1.0.0-beta" < "1.0.0").
        if (PreRelease == other.PreRelease) return 0;
        if (PreRelease.Length == 0) return 1;
        if (other.PreRelease.Length == 0) return -1;
        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public override string ToString() =>
        PreRelease.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
