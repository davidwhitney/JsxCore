namespace JsxCore.Compilation.Assets;

/// <summary>
/// Reads the <c>"use client"</c> / <c>"use server"</c> directive a view opens with.
/// </summary>
/// <remarks>
/// <para>
/// A directive prologue is the run of string-literal statements a module may begin with, which
/// JavaScript has always used for <c>"use strict"</c>. TypeScript emits it through unchanged, so
/// this reads the compiled output rather than the source: the answer is the same, it costs a scan
/// of text the linker has already read, and it is still there on a server published with no
/// <c>.tsx</c> files on it.
/// </para>
/// <para>
/// A directive is a default, not an instruction. The mode a response actually renders in is decided
/// by <see cref="JsxCore.Rendering.JsxViewRenderer"/>, where an explicit mode at the call site wins.
/// </para>
/// </remarks>
public static class ViewDirectives
{
    public const string Client = "use client";
    public const string Server = "use server";

    /// <summary>
    /// The mode a module's prologue asks for, or null when it opens with neither directive.
    /// </summary>
    /// <remarks>
    /// The first recognised directive wins, which is how a prologue behaves anywhere else. Comments
    /// above it are skipped, because a licence header before <c>"use client"</c> is ordinary and
    /// leaves it a prologue as far as the language is concerned.
    /// </remarks>
    public static RenderMode? Parse(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var index = 0;
        while (index < source.Length)
        {
            index = SkipTrivia(source, index);
            if (index >= source.Length)
            {
                return null;
            }

            var quote = source[index];
            if (quote != '"' && quote != '\'')
            {
                // The prologue ended at the first statement that is not a string literal.
                return null;
            }

            var close = source.IndexOf(quote, index + 1);
            if (close < 0)
            {
                return null;
            }

            var directive = source[(index + 1)..close];
            switch (directive)
            {
                case Client:
                    return RenderMode.Client;
                case Server:
                    return RenderMode.Server;
            }

            // Some other directive, "use strict" say. Skip it and keep reading the prologue.
            index = close + 1;
            index = SkipTrivia(source, index);
            if (index < source.Length && source[index] == ';')
            {
                index++;
            }
        }

        return null;
    }

    private static int SkipTrivia(string source, int index)
    {
        while (index < source.Length)
        {
            var c = source[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
            }
            else if (c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                var end = source.IndexOf('\n', index);
                index = end < 0 ? source.Length : end + 1;
            }
            else if (c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
            }
            else
            {
                return index;
            }
        }

        return index;
    }
}
