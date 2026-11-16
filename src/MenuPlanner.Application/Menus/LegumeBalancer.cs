using MenuPlanner.Domain;

namespace MenuPlanner.Application.Menus;

public sealed record LegumeDish(string RecipeId, int Servings, LegumeMode Mode);

public sealed record LegumeBalance(
    IReadOnlyDictionary<string, int> SideServings,
    int TotalServings,
    int LegumeServings,
    int MinServings,
    int MaxServings,
    double MealsPerWeekEquivalent,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Aplica la regla semanal de leguminosa (p. ej. 2–3 de 7 comidas) sobre las porciones del lote, no sobre platillos:
/// cuenta las porciones de guisados con leguminosa incluida y completa con acompañamiento (frijolitos) donde se pueda.
/// </summary>
public static class LegumeBalancer
{
    public static LegumeBalance Balance(IReadOnlyList<LegumeDish> dishes, WeeklyFrequency? frequency, int mealsPerWeek)
    {
        var total = dishes.Sum(d => d.Servings);
        var sides = dishes.ToDictionary(d => d.RecipeId, _ => 0);
        var included = dishes.Where(d => d.Mode == LegumeMode.Included).Sum(d => d.Servings);

        if (frequency is null || total == 0 || mealsPerWeek <= 0)
        {
            return new LegumeBalance(sides, total, included, 0, total, 0, []);
        }

        var minServings = (int)Math.Ceiling((total * (double)frequency.Min / mealsPerWeek) - 1e-9);
        var maxServings = Math.Max(minServings, (int)Math.Floor((total * (double)frequency.Max / mealsPerWeek) + 1e-9));
        var desired = Math.Clamp(
            (int)Math.Round(total * ((frequency.Min + frequency.Max) / 2.0) / mealsPerWeek, MidpointRounding.AwayFromZero),
            minServings,
            maxServings);

        var remaining = Math.Max(0, desired - included);
        foreach (var dish in dishes.Where(d => d.Mode == LegumeMode.Side))
        {
            var give = Math.Min(dish.Servings, remaining);
            sides[dish.RecipeId] = give;
            remaining -= give;
        }

        var legumeServings = included + sides.Values.Sum();
        var warnings = new List<string>();
        if (legumeServings < minServings)
        {
            warnings.Add(
                $"Leguminosa: {legumeServings} de {total} porciones; el plan pide al menos {minServings}. " +
                "Agrega un platillo con frijol, lenteja o garbanzo (o uno que acepte frijolitos de acompañamiento).");
        }
        else if (legumeServings > maxServings)
        {
            warnings.Add(
                $"Leguminosa: {legumeServings} de {total} porciones; es más de lo recomendado ({maxServings}). " +
                "Considera cambiar un platillo con leguminosa.");
        }

        var perWeek = Math.Round(legumeServings / (double)total * mealsPerWeek, 2);
        return new LegumeBalance(sides, total, legumeServings, minServings, maxServings, perWeek, warnings);
    }
}
