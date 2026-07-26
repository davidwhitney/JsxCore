namespace JsxCore.Tool.Cli;

/// <summary>
/// The command line shape of <c>dotnet package add</c>: a positional package name alongside
/// options that take values.
/// </summary>
/// <remarks>
/// Deliberately not the <see cref="Arguments"/> parser, which reads the strict
/// <c>--key value</c> form the MSBuild targets emit. This one has to accept what a person types,
/// including <c>--version 1.2.3</c>, <c>--version=1.2.3</c> and npm's own <c>marked@^12</c>.
/// </remarks>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options;

    /// <summary>Options that consume the next argument. Everything else is a flag.</summary>
    private static readonly HashSet<string> TakesValue =
        new(StringComparer.OrdinalIgnoreCase) { "version", "v", "project", "prefix", "registry" };

    public IReadOnlyList<string> Positional { get; }

    private CommandLine(IReadOnlyList<string> positional, Dictionary<string, string?> options)
    {
        Positional = positional;
        _options = options;
    }

    public static CommandLine Parse(IEnumerable<string> args)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var enumerator = args.GetEnumerator();

        while (enumerator.MoveNext())
        {
            var argument = enumerator.Current;

            if (!argument.StartsWith('-'))
            {
                positional.Add(argument);
                continue;
            }

            var name = argument.TrimStart('-');
            var equals = name.IndexOf('=');

            if (equals >= 0)
            {
                options[name[..equals]] = name[(equals + 1)..];
                continue;
            }

            options[name] = TakesValue.Contains(name) && enumerator.MoveNext()
                ? enumerator.Current
                : null;
        }

        return new CommandLine(positional, options);
    }

    public bool Has(params string[] names) => names.Any(_options.ContainsKey);

    public string? Value(params string[] names) =>
        names.Select(name => _options.GetValueOrDefault(name)).FirstOrDefault(value => value is not null);
}
