using System.Text;

namespace JsxCore.Compilation.Modules;

/// <summary>
/// An import specifier in a compiled module: what it says, and where its text sits in the source.
/// </summary>
/// <param name="Start">Index of the first character inside the quotes.</param>
/// <param name="Length">How many characters the specifier occupies, excluding the quotes.</param>
public readonly record struct ModuleSpecifier(int Start, int Length, string Value)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds the import specifiers in a compiled JavaScript module, and rewrites them.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a regular expression cannot do it. <c>from "./x.css"</c> is an import when
/// the compiler wrote it and a piece of text when a view is displaying it, and the two are
/// indistinguishable without knowing whether the position is code or a string. A view rendering
/// <c>&lt;code&gt;import "./styles.css"&lt;/code&gt;</c> had its own sample rewritten, and generated
/// a module for it.
/// </para>
/// <para>
/// So this walks the source instead: strings, template literals, comments and regular expressions
/// are recognised and skipped, and a specifier is only reported where an import could appear. Not a
/// JavaScript parser and it does not need to be, having one question to answer over input the
/// compiler produced rather than input anyone hand-wrote.
/// </para>
/// </remarks>
public static class ModuleSpecifiers
{
    /// <summary>Every specifier the module imports from, in the order they appear.</summary>
    public static IReadOnlyList<ModuleSpecifier> Scan(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<ModuleSpecifier>();
        ScanCode(source, 0, found, stopAtBrace: false);
        return found;
    }

    /// <summary>
    /// Replaces the specifiers a rewriter returns a value for, leaving the rest of the module
    /// exactly as the compiler emitted it.
    /// </summary>
    public static string Rewrite(
        string source, IReadOnlyList<ModuleSpecifier> specifiers, Func<ModuleSpecifier, string?> rewriter)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(specifiers);
        ArgumentNullException.ThrowIfNull(rewriter);

        var builder = new StringBuilder(source.Length);
        var copied = 0;

        foreach (var specifier in specifiers)
        {
            if (rewriter(specifier) is not { } replacement)
            {
                continue;
            }

            builder.Append(source, copied, specifier.Start - copied);
            builder.Append(replacement);
            copied = specifier.End;
        }

        // Nothing matched, so the original string is the answer and no copy was needed.
        if (copied == 0)
        {
            return source;
        }

        builder.Append(source, copied, source.Length - copied);
        return builder.ToString();
    }

    /// <summary>
    /// Walks code, skipping everything that only looks like code.
    /// </summary>
    /// <param name="stopAtBrace">
    /// Set while inside a <c>${...}</c> substitution, where the closing brace hands control back to
    /// the template that opened it.
    /// </param>
    /// <returns>Where scanning stopped.</returns>
    private static int ScanCode(string source, int index, List<ModuleSpecifier> found, bool stopAtBrace)
    {
        var depth = 0;

        // What a '/' means depends on what came before it: after a value it divides, and after an
        // operator it opens a regular expression.
        var previous = '\0';
        var previousWord = string.Empty;

        while (index < source.Length)
        {
            var current = source[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            switch (current)
            {
                case '/' when index + 1 < source.Length && source[index + 1] == '/':
                    index = SkipLineComment(source, index);
                    continue;

                case '/' when index + 1 < source.Length && source[index + 1] == '*':
                    index = SkipBlockComment(source, index);
                    continue;

                case '/' when StartsValue(previous, previousWord):
                    index = SkipRegex(source, index);
                    previous = '/';
                    previousWord = string.Empty;
                    continue;

                case '"' or '\'':
                    index = SkipString(source, index);
                    previous = current;
                    previousWord = string.Empty;
                    continue;

                case '`':
                    index = ScanTemplate(source, index, found);
                    previous = '`';
                    previousWord = string.Empty;
                    continue;

                case '{':
                    depth++;
                    break;

                case '}' when depth == 0 && stopAtBrace:
                    return index + 1;

                case '}':
                    depth--;
                    break;
            }

            if (IsIdentifierStart(current))
            {
                var end = index;
                while (end < source.Length && IsIdentifierPart(source[end]))
                {
                    end++;
                }

                var word = source[index..end];

                // A property named "from", as in Array.from(...), is not a keyword. Without this
                // the call reads as an import of whatever string it was handed.
                if (previous != '.' && TryReadSpecifier(source, end, word, out var specifier, out var after))
                {
                    found.Add(specifier);
                    index = after;
                    previous = '"';
                    previousWord = string.Empty;
                    continue;
                }

                index = end;
                previous = source[end - 1];
                previousWord = word;
                continue;
            }

            previous = current;
            previousWord = string.Empty;
            index++;
        }

        return index;
    }

    /// <summary>
    /// Reads the specifier that follows an <c>import</c> or <c>from</c> keyword, if one does.
    /// </summary>
    private static bool TryReadSpecifier(
        string source, int index, string word, out ModuleSpecifier specifier, out int after)
    {
        specifier = default;
        after = index;

        if (word is not ("import" or "from"))
        {
            return false;
        }

        // Only import may be parenthesised, as a dynamic import() is. "from" never is, which is
        // what keeps Array.from("abc") from reading as an import of "abc".
        var parenthesised = word == "import";

        var position = SkipWhitespaceAndComments(source, index);

        if (parenthesised && position < source.Length && source[position] == '(')
        {
            position = SkipWhitespaceAndComments(source, position + 1);
        }

        if (position >= source.Length || source[position] is not ('"' or '\''))
        {
            return false;
        }

        var end = SkipString(source, position);

        // Unterminated: it ran into a newline or off the end, so it is not a specifier.
        if (end - position < 2 || end > source.Length || source[end - 1] != source[position])
        {
            return false;
        }

        var start = position + 1;
        var length = end - position - 2;

        specifier = new ModuleSpecifier(start, length, source.Substring(start, length));
        after = end;
        return true;
    }

    private static int ScanTemplate(string source, int index, List<ModuleSpecifier> found)
    {
        index++;

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '\\')
            {
                index += 2;
                continue;
            }

            if (current == '`')
            {
                return index + 1;
            }

            // A substitution is code again, and may hold an import() of its own.
            if (current == '$' && index + 1 < source.Length && source[index + 1] == '{')
            {
                index = ScanCode(source, index + 2, found, stopAtBrace: true);
                continue;
            }

            index++;
        }

        return index;
    }

    private static int SkipString(string source, int index)
    {
        var quote = source[index];
        index++;

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '\\')
            {
                index += 2;
                continue;
            }

            // An unescaped newline cannot appear in one of these, so treat it as unterminated
            // rather than swallowing the rest of the file.
            if (current == '\n' || current == quote)
            {
                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static int SkipRegex(string source, int index)
    {
        index++;
        var inClass = false;

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '\\')
            {
                index += 2;
                continue;
            }

            switch (current)
            {
                case '\n':
                    return index;
                case '[':
                    inClass = true;
                    break;
                case ']':
                    inClass = false;
                    break;

                // A '/' inside a character class is a literal, as in /[/]/.
                case '/' when !inClass:
                    return index + 1;
            }

            index++;
        }

        return index;
    }

    private static int SkipLineComment(string source, int index)
    {
        var end = source.IndexOf('\n', index);
        return end < 0 ? source.Length : end + 1;
    }

    private static int SkipBlockComment(string source, int index)
    {
        var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
        return end < 0 ? source.Length : end + 2;
    }

    private static int SkipWhitespaceAndComments(string source, int index)
    {
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    index = SkipLineComment(source, index);
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    index = SkipBlockComment(source, index);
                    continue;
                }
            }

            return index;
        }

        return index;
    }

    /// <summary>
    /// Whether a value could begin here, which is what decides a '/' between a regular expression
    /// and a division.
    /// </summary>
    private static bool StartsValue(char previous, string previousWord) =>
        previous is '\0' or '(' or ',' or '=' or ':' or '[' or '!' or '&' or '|' or '?'
            or '{' or '}' or ';' or '+' or '-' or '*' or '%' or '^' or '~' or '<' or '>'
        || previousWord is "return" or "typeof" or "instanceof" or "in" or "of" or "new"
            or "delete" or "void" or "case" or "do" or "else" or "yield" or "await";

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value is '_' or '$';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';
}
