namespace Menuki.Engine;

/// <summary>
/// The same keys in the Russian (ЙЦУКЕН) and English (QWERTY) layouts, so a search typed in the wrong
/// layout still finds its item: "кузщ" is "repo" typed with the Russian layout on.
/// </summary>
public static class KeyboardLayout
{
    private const string Latin = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
    private const string Cyrillic = "ёйцукенгшщзхъфывапролджэячсмитьбюЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ";

    private static readonly Dictionary<char, char> ToLatin = Map(Cyrillic, Latin);
    private static readonly Dictionary<char, char> ToCyrillic = Map(Latin, Cyrillic);

    private static Dictionary<char, char> Map(string from, string to)
    {
        var map = new Dictionary<char, char>();
        for (var i = 0; i < from.Length; i++) map[from[i]] = to[i];
        return map;
    }

    /// <summary>Russian-layout letters turned into the English keys at the same place; other characters stay.</summary>
    public static string AsLatin(string text) => Convert(text, ToLatin);

    /// <summary>English-layout keys turned into the Russian letters at the same place; other characters stay.</summary>
    public static string AsCyrillic(string text) => Convert(text, ToCyrillic);

    private static string Convert(string text, Dictionary<char, char> map) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = map.TryGetValue(source[i], out var c) ? c : source[i];
        });

    /// <summary>
    /// True when <paramref name="text"/> contains <paramref name="query"/> as typed, or as typed in the other
    /// layout, ignoring case.
    /// </summary>
    public static bool Matches(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase)
        || text.Contains(AsLatin(query), StringComparison.OrdinalIgnoreCase)
        || text.Contains(AsCyrillic(query), StringComparison.OrdinalIgnoreCase);
}
