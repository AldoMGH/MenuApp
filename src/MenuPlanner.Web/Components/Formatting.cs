using System.Globalization;
using MenuPlanner.Domain;

namespace MenuPlanner.Web.Components;

/// <summary>Textos en español para la UI.</summary>
public static class Formatting
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public static string Week(DateOnly weekOf) => weekOf.ToString("d 'de' MMMM yyyy", Mx);

    public static string Date(DateTime? date) => date?.ToString("d MMM yyyy", Mx) ?? "—";

    public static string Status(MenuStatus status) => status == MenuStatus.Confirmed ? "Confirmado" : "Borrador";

    public static string Group(ComponentGroup? group) => group switch
    {
        ComponentGroup.Verdura => "Verdura",
        ComponentGroup.Proteina => "Proteína",
        ComponentGroup.Leguminosa => "Leguminosa",
        ComponentGroup.Cereal => "Cereal / tubérculo",
        ComponentGroup.Grasa => "Grasa saludable",
        null => "Fijo (salsa, condimento)",
        _ => group.ToString()!,
    };

    public static string Unit(Unit unit) => unit switch
    {
        Domain.Unit.G => "g",
        Domain.Unit.Ml => "ml",
        Domain.Unit.Pieza => "pza",
        Domain.Unit.Taza => "tz",
        Domain.Unit.Cda => "cda",
        Domain.Unit.Cdita => "cdita",
        Domain.Unit.Porcion => "porción",
        Domain.Unit.Diente => "diente",
        Domain.Unit.Paquete => "paquete",
        Domain.Unit.Lata => "lata",
        _ => unit.ToString().ToLowerInvariant(),
    };

    public static string Quantity(double value, Unit unit) =>
        unit switch
        {
            Domain.Unit.G when value >= 1000 => $"{(value / 1000).ToString("0.##", Mx)} kg",
            Domain.Unit.Ml when value >= 1000 => $"{(value / 1000).ToString("0.##", Mx)} L",
            _ => $"{value.ToString("0.##", Mx)} {Unit(unit)}",
        };

    public static string Compatibility(DishCompatibility compatibility) => compatibility switch
    {
        DishCompatibility.Adaptada => "Adaptada",
        DishCompatibility.NoCompatible => "No compatible",
        _ => "Compatible",
    };

    public static MudBlazor.Color CompatibilityColor(DishCompatibility compatibility) => compatibility switch
    {
        DishCompatibility.Adaptada => MudBlazor.Color.Warning,
        DishCompatibility.NoCompatible => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Success,
    };

    public static readonly IReadOnlyList<(string Value, string Label)> ProteinFamilies =
    [
        ("pollo", "Pollo"),
        ("res", "Res"),
        ("cerdo", "Cerdo"),
        ("pescado", "Pescado"),
        ("mariscos", "Mariscos"),
    ];
}
