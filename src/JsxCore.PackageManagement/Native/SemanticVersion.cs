using System.Globalization;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string prerelease, string build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public string Build { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        if (span.StartsWith('v') || span.StartsWith('='))
        {
            span = span[1..].Trim();
        }

        var build = string.Empty;
        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            build = span[(plus + 1)..];
            span = span[..plus];
        }

        var prerelease = string.Empty;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = span[(dash + 1)..];
            span = span[..dash];
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        // Missing parts are zero, so "1" and "1.2" parse. npm accepts both in ranges.
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length)
            {
                numbers[i] = 0;
            }
            else if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease, build);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a version.");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var byNumber = Major.CompareTo(other.Major);
        if (byNumber != 0) return byNumber;
        byNumber = Minor.CompareTo(other.Minor);
        if (byNumber != 0) return byNumber;
        byNumber = Patch.CompareTo(other.Patch);
        if (byNumber != 0) return byNumber;

        // A release outranks any prerelease of the same numbers.
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            // Fewer identifiers sorts lower, so 1.0.0-alpha precedes 1.0.0-alpha.1.
            if (i >= a.Length) return -1;
            if (i >= b.Length) return 1;

            var leftNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var rightNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);

            // Numeric identifiers always sort below alphanumeric ones.
            if (leftNumeric && rightNumeric)
            {
                var byValue = an.CompareTo(bn);
                if (byValue != 0) return byValue;
            }
            else if (leftNumeric) return -1;
            else if (rightNumeric) return 1;
            else
            {
                var byText = string.CompareOrdinal(a[i], b[i]);
                if (byText != 0) return Math.Sign(byText);
            }
        }

        return 0;
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator ==(SemanticVersion? a, SemanticVersion? b) => a?.CompareTo(b) == 0 || (a is null && b is null);
    public static bool operator !=(SemanticVersion? a, SemanticVersion? b) => !(a == b);

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" +
        (IsPrerelease ? "-" + Prerelease : string.Empty) +
        (Build.Length > 0 ? "+" + Build : string.Empty);
}
