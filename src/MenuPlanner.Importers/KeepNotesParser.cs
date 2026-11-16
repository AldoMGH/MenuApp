using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MenuPlanner.Domain;

namespace MenuPlanner.Importers;

public sealed class ParsedNote
{
    public required int Position { get; init; }

    public required string Title { get; init; }

    public required int Month { get; init; }

    public int? Week { get; init; }

    public int Year { get; set; }

    public DateTime Date { get; set; }

    public List<ParsedItem> Items { get; } = [];

    /// <summary>Hash del título y el contenido normalizados: identifica la nota entre importaciones.</summary>
    public string Hash { get; set; } = string.Empty;
}

public sealed record ParsedItem(NoteSection Section, string Text);

public sealed class ParseResult
{
    public List<ParsedNote> Notes { get; } = [];

    public List<string> Warnings { get; } = [];

    public int LatestYear { get; set; }
}

/// <summary>
/// Lee el texto de tus notas de Keep copiadas a Google Docs (exportado como Markdown o texto, o leído con el conector).
/// Cada nota: un título "Mes N" (semana del mes, sin año) y secciones Comidas / Carnes / Verduras / Procesados / Limpieza.
/// Las notas vienen de la más reciente a la más antigua; el año se deduce de ese orden.
/// </summary>
public static partial class KeepNotesParser
{
    private static readonly string[] MonthNames =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static readonly Dictionary<string, NoteSection?> Sections = new(StringComparer.Ordinal)
    {
        ["comidas"] = NoteSection.Comidas,
        ["comida"] = NoteSection.Comidas,
        ["platillos"] = NoteSection.Comidas,
        ["carnes"] = NoteSection.Carnes,
        ["carne"] = NoteSection.Carnes,
        ["vegetales"] = NoteSection.Verduras,
        ["verduras"] = NoteSection.Verduras,
        ["verdura"] = NoteSection.Verduras,
        ["frutas"] = NoteSection.Verduras,
        ["fruta"] = NoteSection.Verduras,
        ["frutas y verduras"] = NoteSection.Verduras,
        ["procesados"] = NoteSection.Procesados,
        ["abarrotes"] = NoteSection.Procesados,
        ["lacteos"] = NoteSection.Procesados,
        ["limpieza"] = null, // se ignora (solo alimentos)
        ["otros"] = null,
    };

    public static ParseResult Parse(string text, int? latestYear = null, DateTime? today = null)
    {
        var result = new ParseResult();
        ParsedNote? current = null;
        NoteSection? section = null;
        var ignoring = false;

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (Separator().IsMatch(rawLine))
            {
                Close(current, result);
                current = null;
                section = null;
                ignoring = false;
                continue;
            }

            var line = StripBullet(TextNormalizer.Clean(rawLine));
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseTitle(line, out var month, out var week))
            {
                Close(current, result);
                current = new ParsedNote { Position = result.Notes.Count, Title = line, Month = month, Week = week };
                section = null;
                ignoring = false;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (Sections.TryGetValue(TextNormalizer.Key(line), out var newSection))
            {
                section = newSection;
                ignoring = newSection is null;
                continue;
            }

            if (ignoring || section is null)
            {
                continue;
            }

            current.Items.Add(new ParsedItem(section.Value, line));
        }

        Close(current, result);
        InferDates(result, latestYear, today ?? DateTime.Today);
        return result;
    }

    public static bool TryParseTitle(string line, out int month, out int? week)
    {
        month = 0;
        week = null;
        var match = Title().Match(TextNormalizer.Key(line));
        if (!match.Success)
        {
            return false;
        }

        month = Array.IndexOf(MonthNames, match.Groups["mes"].Value) + 1;
        if (match.Groups["semana"].Success)
        {
            week = int.Parse(match.Groups["semana"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return month > 0;
    }

    /// <summary>
    /// Año: la primera nota (la más reciente) es del año <paramref name="latestYear"/> o, si no se indica, del año actual
    /// (o del anterior si su mes todavía no llega). Al avanzar hacia notas más antiguas, si el mes "sube" se cambia de año.
    /// Fecha: lunes de la semana N del mes (sin semana: el primer lunes).
    /// </summary>
    public static void InferDates(ParseResult result, int? latestYear, DateTime today)
    {
        if (result.Notes.Count == 0)
        {
            result.LatestYear = latestYear ?? today.Year;
            return;
        }

        var year = latestYear ?? (result.Notes[0].Month > today.Month ? today.Year - 1 : today.Year);
        result.LatestYear = year;
        ParsedNote? previous = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var note in result.Notes)
        {
            if (previous is not null &&
                (note.Month > previous.Month || (note.Month == previous.Month && (note.Week ?? 0) > (previous.Week ?? 0) && note.Week is not null && previous.Week is not null)))
            {
                if (note.Month > previous.Month)
                {
                    year--;
                }
                else
                {
                    result.Warnings.Add($"'{note.Title}' viene después de '{previous.Title}' pero es una semana posterior; revisa el orden.");
                }
            }

            note.Year = year;
            note.Date = WeekStart(year, note.Month, note.Week);
            var label = $"{note.Month}-{note.Week}-{year}";
            if (!seen.Add(label))
            {
                result.Warnings.Add($"Hay dos notas con el título '{note.Title}' ({year}); se importan ambas con la misma fecha.");
            }

            previous = note;
        }
    }

    public static DateTime WeekStart(int year, int month, int? week)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = ((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7;
        var date = first.AddDays(offset + (7 * (Math.Clamp(week ?? 1, 1, 5) - 1)));
        return date.Month == month ? date : date.AddDays(-7);
    }

    private static void Close(ParsedNote? note, ParseResult result)
    {
        if (note is null)
        {
            return;
        }

        var sb = new StringBuilder(TextNormalizer.Key(note.Title));
        foreach (var item in note.Items)
        {
            sb.Append('\n').Append(item.Section).Append(':').Append(TextNormalizer.Key(item.Text));
        }

        note.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..24].ToLowerInvariant();
        if (note.Items.Count == 0)
        {
            result.Warnings.Add($"La nota '{note.Title}' no tiene renglones reconocibles.");
        }

        result.Notes.Add(note);
    }

    private static string StripBullet(string line) => Bullet().Replace(line, string.Empty).Trim();

    // Líneas horizontales: "-----", "___", "***", "* * *".
    [GeneratedRegex(@"^\s*([-_*=─—]\s*){3,}\s*$")]
    private static partial Regex Separator();

    // Viñetas: "- ", "* ", "• ", "● ", "○ ", "◦ ", "▪ ", "☐ ", "☑ ", "[ ] ", "[x] ", "1. ".
    [GeneratedRegex(@"^(\s*([-*•●○◦▪☐☑✓✔]|\[[ xX]?\]|\d+[.)])\s+)+")]
    private static partial Regex Bullet();

    // "noviembre 1", "lista julio - 2", "octubre", "lista mayo 2" (clave ya normalizada).
    [GeneratedRegex(@"^(lista\s+)?(?<mes>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre)(\s*-?\s*(?<semana>[1-5]))?$")]
    private static partial Regex Title();
}
