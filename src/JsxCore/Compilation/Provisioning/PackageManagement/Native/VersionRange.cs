using System.Globalization;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public enum ComparatorKind { GreaterOrEqual, Greater, LessOrEqual, Less, Exact }

public sealed record Comparator(ComparatorKind Kind, SemanticVersion Version)
{
    public bool Allows(SemanticVersion version) => Kind switch
    {
        ComparatorKind.GreaterOrEqual => version >= Version,
        ComparatorKind.Greater => version > Version,
        ComparatorKind.LessOrEqual => version <= Version,
        ComparatorKind.Less => version < Version,
        _ => version == Version
    };
}

public sealed class VersionRange
{
    // A range is an OR of AND-sets, which is what "^1 || ~2.3" means.
    private readonly IReadOnlyList<IReadOnlyList<Comparator>> _sets;

    private VersionRange(IReadOnlyList<IReadOnlyList<Comparator>> sets, string text)
    {
        _sets = sets;
        Text = text;
    }

    public string Text { get; }

    // Anything npm understands but this does not: git urls, npm: aliases, file paths, tags.
    public bool IsUnsupported => _sets.Count == 0;

    public static VersionRange Parse(string? text)
    {
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0 || raw == "*" || raw == "x" || raw == "latest")
        {
            return new VersionRange([[]], raw.Length == 0 ? "*" : raw);
        }

        if (raw.Contains(':') || raw.StartsWith("./") || raw.StartsWith("../") || raw.StartsWith('/'))
        {
            return new VersionRange([], raw);
        }

        // An operator may be written apart from its version, as in ">= 4.21.0". Joining them up
        // first means the comparator parser only has to handle one shape.
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @"(>=|<=|>|<|=|\^|~)\s+", "$1");

        var sets = new List<IReadOnlyList<Comparator>>();
        foreach (var alternative in raw.Split("||", StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseSet(alternative.Trim()) is { } set)
            {
                sets.Add(set);
            }
        }

        return new VersionRange(sets, raw);
    }

    private static List<Comparator>? ParseSet(string text)
    {
        // "1.2.3 - 2.3.4" is an inclusive range, and the only place a bare dash is an operator.
        var hyphen = text.Split(" - ", StringSplitOptions.TrimEntries);
        if (hyphen.Length == 2)
        {
            if (!SemanticVersion.TryParse(hyphen[0], out var from))
            {
                return null;
            }

            var upper = UpperBoundOfPartial(hyphen[1]);
            return SemanticVersion.TryParse(hyphen[1], out var to)
                ? [new Comparator(ComparatorKind.GreaterOrEqual, from),
                   upper is null
                       ? new Comparator(ComparatorKind.LessOrEqual, to)
                       : new Comparator(ComparatorKind.Less, upper)]
                : null;
        }

        var comparators = new List<Comparator>();
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ParseComparator(part, comparators))
            {
                return null;
            }
        }

        return comparators;
    }

    private static bool ParseComparator(string part, List<Comparator> into)
    {
        if (part is "*" or "x" or "X" or "")
        {
            return true;
        }

        if (part[0] is '^' or '~')
        {
            return ParseCaretOrTilde(part[0], part[1..], into);
        }

        foreach (var (prefix, kind) in new[]
                 {
                     (">=", ComparatorKind.GreaterOrEqual), ("<=", ComparatorKind.LessOrEqual),
                     (">", ComparatorKind.Greater), ("<", ComparatorKind.Less), ("=", ComparatorKind.Exact)
                 })
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (!SemanticVersion.TryParse(part[prefix.Length..], out var bound))
                {
                    return false;
                }
                into.Add(new Comparator(kind, bound));
                return true;
            }
        }

        // A bare partial version is a range: "1.2" means >=1.2.0 <1.3.0, "1" means >=1.0.0 <2.0.0.
        if (UpperBoundOfPartial(part) is { } ceiling)
        {
            if (!SemanticVersion.TryParse(part.Replace("x", "0").Replace("X", "0").Replace("*", "0"), out var floor))
            {
                return false;
            }
            into.Add(new Comparator(ComparatorKind.GreaterOrEqual, floor));
            into.Add(new Comparator(ComparatorKind.Less, ceiling));
            return true;
        }

        if (!SemanticVersion.TryParse(part, out var exact))
        {
            return false;
        }

        into.Add(new Comparator(ComparatorKind.Exact, exact));
        return true;
    }

    private static bool ParseCaretOrTilde(char op, string text, List<Comparator> into)
    {
        var given = text.Split('.');
        var specified = given.TakeWhile(p => p is not ("x" or "X" or "*")).Count();

        if (!SemanticVersion.TryParse(text.Replace("x", "0").Replace("X", "0").Replace("*", "0"), out var from))
        {
            return false;
        }

        into.Add(new Comparator(ComparatorKind.GreaterOrEqual, from));

        // Tilde allows patch releases when a minor was given, and minor releases when it was not.
        // Caret allows changes that do not alter the leftmost non-zero component, which is why
        // ^0.2.3 is stricter than ^1.2.3, and why ^0 and ^0.0 differ from each other.
        var ceiling = op == '~'
            ? Bump(from, specified >= 2 ? 1 : 0)
            : from.Major > 0 ? Bump(from, 0)
            : from.Minor > 0 ? Bump(from, 1)
            : from.Patch > 0 ? Bump(from, 2)
            : Bump(from, Math.Min(specified, 3) - 1);

        into.Add(new Comparator(ComparatorKind.Less, ceiling));
        return true;
    }

    private static SemanticVersion Bump(SemanticVersion version, int component) => component switch
    {
        0 => SemanticVersion.Parse($"{version.Major + 1}.0.0"),
        1 => SemanticVersion.Parse($"{version.Major}.{version.Minor + 1}.0"),
        _ => SemanticVersion.Parse($"{version.Major}.{version.Minor}.{version.Patch + 1}")
    };

    private static SemanticVersion? UpperBoundOfPartial(string text)
    {
        var parts = text.Split('.');
        var explicitParts = parts.TakeWhile(p => p is not ("x" or "X" or "*")).ToArray();

        if (explicitParts.Length == parts.Length && parts.Length == 3)
        {
            return null;
        }

        if (explicitParts.Length == 0)
        {
            return null;
        }

        var numbers = explicitParts
            .Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .ToArray();

        if (numbers.Any(n => n < 0))
        {
            return null;
        }

        return numbers.Length == 1
            ? SemanticVersion.Parse($"{numbers[0] + 1}.0.0")
            : SemanticVersion.Parse($"{numbers[0]}.{numbers[1] + 1}.0");
    }

    public bool Satisfies(SemanticVersion version) => _sets.Any(set => SetAllows(set, version));

    private static bool SetAllows(IReadOnlyList<Comparator> set, SemanticVersion version)
    {
        if (!set.All(c => c.Allows(version)))
        {
            return false;
        }

        // A prerelease only satisfies a set that mentions a prerelease of the same three numbers.
        // Without this, ^1.0.0 would quietly accept 2.0.0-beta, which npm does not.
        if (version.IsPrerelease)
        {
            return set.Any(c => c.Version.IsPrerelease
                                && c.Version.Major == version.Major
                                && c.Version.Minor == version.Minor
                                && c.Version.Patch == version.Patch);
        }

        return true;
    }

    public SemanticVersion? Best(IEnumerable<SemanticVersion> candidates) =>
        candidates.Where(Satisfies).OrderByDescending(v => v).FirstOrDefault();

    public override string ToString() => Text;
}
