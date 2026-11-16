using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MenuPlanner.Importers;

/// <summary>Limpieza y normalización de texto de notas: emojis, mojibake, markdown, acentos y abreviaturas.</summary>
public static partial class TextNormalizer
{
    private static readonly HashSet<string> StopWords = ["de", "con", "la", "el", "los", "las", "a", "al", "en", "y", "o", "del"];

    /// <summary>Quita markdown, emojis y basura de codificación; conserva acentos y mayúsculas (para mostrar).</summary>
    public static string Clean(string text)
    {
        var s = text.Replace("<!-- end list -->", string.Empty, StringComparison.Ordinal);
        s = MarkdownEscape().Replace(s, "$1");
        s = s.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal);
        s = Mojibake().Replace(s, " ");

        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.PrivateUse)
            {
                sb.Append(' ');
                continue;
            }

            if (rune.Value is 0xFE0F or 0x200D)
            {
                continue;
            }

            sb.Append(rune.ToString());
        }

        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Clave de comparación: minúsculas, sin acentos, sin puntuación (salvo "/" y "+"), "c/" → "con".</summary>
    public static string Key(string text)
    {
        var s = RemoveAccents(Clean(text).ToLowerInvariant());
        s = WithSlash().Replace(s, " con ");
        s = WithLetter().Replace(s, "con");
        s = NonWord().Replace(s, " ");
        s = Spaces().Replace(s, " ").Trim();
        s = SlashSpaces().Replace(s, "$1");
        return s.Trim(' ', '/', '+');
    }

    /// <summary>Reemplaza palabras completas (separadas por espacio, "/" o "+") según el diccionario.</summary>
    public static string ReplaceWords(string key, IReadOnlyDictionary<string, string> replacements) =>
        replacements.Count == 0 ? key : Word().Replace(key, m => replacements.TryGetValue(m.Value, out var to) ? to : m.Value);

    public static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string[] Tokens(string key) =>
        key.Split(new[] { ' ', '/', '+' }, StringSplitOptions.RemoveEmptyEntries);

    public static string[] MeaningfulTokens(string key) =>
        Tokens(key).Where(t => !StopWords.Contains(t)).ToArray();

    /// <summary>Similitud 0–1 basada en distancia de Levenshtein.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        var distance = Levenshtein(a, b);
        return 1.0 - (distance / (double)Math.Max(a.Length, b.Length));
    }

    public static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public static string Slug(string text) =>
        Spaces().Replace(NonWord().Replace(RemoveAccents(Clean(text).ToLowerInvariant()), " "), " ").Trim().Replace(' ', '-').Replace("/", "-", StringComparison.Ordinal).Replace("+", "-", StringComparison.Ordinal);

    // "\+" "\*" "\_" de la exportación a markdown.
    [GeneratedRegex(@"\\([+*_#\-.()\[\]!>`~])")]
    private static partial Regex MarkdownEscape();

    // Emojis UTF-8 leídos como Latin-1: "ð" seguido de bytes de continuación (U+0080–U+00BF).
    [GeneratedRegex(@"ð[\u0080-\u00BF]{1,3}|â[\u0080-\u009F][\u0080-\u00BF]?")]
    private static partial Regex Mojibake();

    [GeneratedRegex(@"(?<=^|\s)c/")]
    private static partial Regex WithSlash();

    [GeneratedRegex(@"(?<=^|\s)c(?=\s)")]
    private static partial Regex WithLetter();

    [GeneratedRegex(@"[^a-z0-9ñ/+ ]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[a-z0-9ñ]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s*([/+])\s*")]
    private static partial Regex SlashSpaces();
}
