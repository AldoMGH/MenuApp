using MenuPlanner.Domain;

namespace MenuPlanner.Portioning;

/// <summary>
/// Suma los macros aproximados del lote a partir de los renglones ya porcionados (en unidad base, crudo):
/// convierte cada renglón a gramos y aplica los valores por 100 g del ingrediente. Solo informativo.
/// </summary>
public sealed class NutritionCalculator(UnitConverter converter)
{
    public RecipeNutrition Calculate(IEnumerable<PortionLine> batch, int servings, IReadOnlyDictionary<string, Ingredient> ingredients)
    {
        var total = new NutritionFacts();
        var missing = new List<string>();

        foreach (var line in batch)
        {
            if (!ingredients.TryGetValue(line.IngredientId, out var ingredient) || ingredient.Nutrition is null)
            {
                missing.Add(line.Name);
                continue;
            }

            double grams;
            try
            {
                grams = converter.Convert(ingredient, line.Quantity, line.Unit, Unit.G);
            }
            catch (ConversionException)
            {
                missing.Add(line.Name);
                continue;
            }

            var factor = grams / 100.0;
            total.Kcal += ingredient.Nutrition.Kcal * factor;
            total.Protein += ingredient.Nutrition.Protein * factor;
            total.Carbs += ingredient.Nutrition.Carbs * factor;
            total.Fat += ingredient.Nutrition.Fat * factor;
            total.Fiber += ingredient.Nutrition.Fiber * factor;
        }

        var divisor = servings > 0 ? servings : 1;
        return new RecipeNutrition
        {
            Batch = Round(total),
            PerServing = Round(Scale(total, 1.0 / divisor)),
            Missing = missing.Distinct().ToList(),
        };
    }

    private static NutritionFacts Scale(NutritionFacts n, double k) =>
        new() { Kcal = n.Kcal * k, Protein = n.Protein * k, Carbs = n.Carbs * k, Fat = n.Fat * k, Fiber = n.Fiber * k };

    private static NutritionFacts Round(NutritionFacts n) => new()
    {
        Kcal = Math.Round(n.Kcal),
        Protein = Math.Round(n.Protein, 1),
        Carbs = Math.Round(n.Carbs, 1),
        Fat = Math.Round(n.Fat, 1),
        Fiber = Math.Round(n.Fiber, 1),
    };
}
