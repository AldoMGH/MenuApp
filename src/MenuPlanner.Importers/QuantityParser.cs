using System.Globalization;
using System.Text.RegularExpressions;
using MenuPlanner.Domain;

namespace MenuPlanner.Importers;

public sealed record ParsedQuantity(double? Qty, Unit? Unit, string Name);

/// <summary>"1 kg piernil", "3/4 kg pechuga", "600g carne", "2L de leche", "3 latas de atún", "2 - 3 jalapeño".</summary>
public static partial class QuantityParser
{
    public static ParsedQuantity Parse(string text)
    {
        var clean = TextNormalizer.Clean(text);
        var match = Pattern().Match(clean);
        if (!match.Success)
        {
            return new ParsedQuantity(null, null, clean);
        }

        var qty = ParseNumber(match.Groups["qty"].Value);
        var unitText = match.Groups["unit"].Value.ToLowerInvariant();
        var name = match.Groups["name"].Value.Trim();
        if (name.Length == 0)
        {
            return new ParsedQuantity(null, null, clean);
        }

        var (unit, factor) = unitText switch
        {
            "kg" or "kilo" or "kilos" => (Unit.G, 1000d),
            "g" or "gr" or "grs" or "gramos" => (Unit.G, 1d),
            "l" or "lt" or "lts" or "litro" or "litros" => (Unit.Ml, 1000d),
            "ml" => (Unit.Ml, 1d),
            "lata" or "latas" => (Unit.Lata, 1d),
            "paquete" or "paquetes" or "paq" => (Unit.Paquete, 1d),
            "pza" or "pzas" or "pieza" or "piezas" => (Unit.Pieza, 1d),
            "" => ((Unit?)null, 1d),
            _ => ((Unit?)null, 1d),
        };

        return new ParsedQuantity(qty is null ? null : Math.Round(qty.Value * factor, 3), unit ?? (qty is null ? null : Unit.Pieza), name);
    }

    private static double? ParseNumber(string text)
    {
        text = text.Replace(',', '.').Trim();
        var range = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (range.Length == 2)
        {
            return ParseNumber(range[1]); // "2 - 3 jalapeño" → el máximo
        }

        if (text.Contains('/', StringComparison.Ordinal))
        {
            var parts = text.Split('/');
            return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                   double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0
                ? a / b
                : null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex(@"^(?<qty>\d+(?:[.,]\d+)?(?:\s*/\s*\d+)?(?:\s*-\s*\d+(?:[.,]\d+)?)?)\s*(?<unit>kg|kilos?|grs?|gramos|g|lts?|litros?|l|ml|latas?|paquetes?|paq|pzas?|piezas?)?\.?\s+(?:de\s+)?(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
