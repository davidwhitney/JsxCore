namespace JsxCore.Tool;

public sealed class Arguments(IReadOnlyDictionary<string, string> values)
{
    public static Arguments Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;

        foreach (var argument in args)
        {
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                key = argument[2..];
                values[key] = string.Empty;
            }
            else if (key is not null)
            {
                values[key] = argument;
                key = null;
            }
        }

        return new Arguments(values);
    }

    public string? Optional(string name) =>
        values.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    public string Required(string name) =>
        Optional(name) ?? throw new ArgumentException($"JsxCore.Tool: --{name} is required.");

    public bool Flag(string name) =>
        Optional(name) is { } value && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    public IReadOnlyList<string> List(string name) =>
        Optional(name)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
