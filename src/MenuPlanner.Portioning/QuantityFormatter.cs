using System.Globalization;
using MenuPlanner.Domain;

namespace MenuPlanner.Portioning;

/// <summary>Redondeo a cantidades medibles/comprables y formato legible ("1.15 kg", "1½ tz", "8 pzas").</summary>
public static class QuantityFormatter
{
    private const double Epsilon = 1e-9;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Redondea hacia arriba a un incremento práctico para cocinar.</summary>
    public static double RoundForCooking(double value, Unit unit)
    {
        if (value <= 0)
        {
            return 0;
        }

        var step = unit switch
        {
            Unit.G or Unit.Ml => value < 50 ? 1 : value < 250 ? 5 : 10,
            _ => 0.25,
        };
        return RoundUpToStep(value, step);
    }

    public static double RoundUpToStep(double value, double step)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (step <= 0)
        {
            return value;
        }

        return Math.Round(Math.Ceiling((value / step) - Epsilon) * step, 6);
    }

    public static string Format(double value, Unit unit, string? label = null)
    {
        switch (unit)
        {
            case Unit.G:
                return value >= 1000
                    ? $"{(value / 1000).ToString("0.##", Culture)} kg"
                    : $"{Math.Round(value).ToString("0", Culture)} g";
            case Unit.Ml:
                return value >= 1000
                    ? $"{(value / 1000).ToString("0.##", Culture)} L"
                    : $"{Math.Round(value).ToString("0", Culture)} ml";
        }

        var number = FormatFraction(value);
        var name = label ?? UnitName(unit, IsPlural(value));
        return $"{number} {name}";
    }

    public static string FormatFraction(double value)
    {
        var whole = Math.Floor(value + Epsilon);
        var fraction = value - whole;
        var symbol = Math.Round(fraction, 2) switch
        {
            0.0 => string.Empty,
            0.25 => "¼",
            0.33 => "⅓",
            0.5 => "½",
            0.67 => "⅔",
            0.75 => "¾",
            _ => null,
        };

        if (symbol is null)
        {
            return value.ToString("0.##", Culture);
        }

        return whole == 0 && symbol.Length > 0 ? symbol : $"{whole.ToString("0", Culture)}{symbol}";
    }

    public static string UnitName(Unit unit, bool plural) => unit switch
    {
        Unit.G => "g",
        Unit.Ml => "ml",
        Unit.Pieza => plural ? "pzas" : "pza",
        Unit.Taza => "tz",
        Unit.Cda => plural ? "cdas" : "cda",
        Unit.Cdita => plural ? "cditas" : "cdita",
        Unit.Porcion => plural ? "porciones" : "porción",
        Unit.Diente => plural ? "dientes" : "diente",
        Unit.Paquete => plural ? "paquetes" : "paquete",
        Unit.Lata => plural ? "latas" : "lata",
        _ => unit.ToString().ToLowerInvariant(),
    };

    private static bool IsPlural(double value) => value > 1 + Epsilon;
}
