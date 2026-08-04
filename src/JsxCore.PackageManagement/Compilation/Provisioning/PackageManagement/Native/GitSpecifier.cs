using System.Text.RegularExpressions;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

// A dependency on a repository rather than on a published package:
//
//   "github:user/repo"        "user/repo"            "gitlab:user/repo#v2"
//   "git+https://github.com/user/repo.git#commit"    "git+ssh://git@github.com/user/repo.git"
//
// Fetched as an archive from the host rather than cloned, so no git has to be installed. That
// covers the hosts npm has shorthands for, which is what people actually use; anything else is
// reported rather than half handled.
public sealed partial record GitSpecifier(string Host, string Owner, string Repository, string Reference)
{
    public string ArchiveUrl => Host switch
    {
        "gitlab" => $"https://gitlab.com/{Owner}/{Repository}/-/archive/{Reference}/{Repository}-{Reference}.tar.gz",
        "bitbucket" => $"https://bitbucket.org/{Owner}/{Repository}/get/{Reference}.tar.gz",
        _ => $"https://codeload.github.com/{Owner}/{Repository}/tar.gz/{Reference}"
    };

    // What npm records, so a lock file written here means the same thing to npm.
    public string ResolvedUrl => Host switch
    {
        "gitlab" => $"git+https://gitlab.com/{Owner}/{Repository}.git#{Reference}",
        "bitbucket" => $"git+https://bitbucket.org/{Owner}/{Repository}.git#{Reference}",
        _ => $"git+https://github.com/{Owner}/{Repository}.git#{Reference}"
    };

    public static bool TryParse(string? text, out GitSpecifier specifier)
    {
        specifier = null!;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        var host = "github";
        foreach (var prefix in new[] { "github:", "gitlab:", "bitbucket:" })
        {
            if (raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                host = prefix[..^1];
                raw = raw[prefix.Length..];
            }
        }

        if (UrlPattern().Match(raw) is { Success: true } url)
        {
            host = url.Groups["host"].Value switch
            {
                var h when h.Contains("gitlab") => "gitlab",
                var h when h.Contains("bitbucket") => "bitbucket",
                _ => "github"
            };
            raw = url.Groups["path"].Value;
        }
        else if (raw.Contains("://") || raw.StartsWith("git@", StringComparison.Ordinal))
        {
            // A git URL on a host with no archive endpoint we know about.
            return false;
        }

        var reference = "HEAD";
        var hash = raw.IndexOf('#');
        if (hash >= 0)
        {
            reference = raw[(hash + 1)..];
            raw = raw[..hash];

            // "#semver:^1.0.0" selects by tag, which needs the tag list. Not handled.
            if (reference.StartsWith("semver:", StringComparison.Ordinal))
            {
                return false;
            }
        }

        raw = raw.TrimEnd('/');
        if (raw.EndsWith(".git", StringComparison.Ordinal))
        {
            raw = raw[..^4];
        }

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !ShorthandPattern().IsMatch(raw))
        {
            return false;
        }

        specifier = new GitSpecifier(host, parts[0], parts[1], reference.Length == 0 ? "HEAD" : reference);
        return true;
    }

    [GeneratedRegex(@"^(git\+)?(https?|ssh|git)://(git@)?(?<host>[^/]+)/(?<path>.+)$")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex ShorthandPattern();
}
