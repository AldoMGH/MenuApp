using MenuPlanner.Domain;
using MenuPlanner.Portioning;

namespace MenuPlanner.Application.Suggestions;

/// <summary>
/// "¿Qué puedo cocinar con lo que tengo?": para cada receta, qué parte de sus ingredientes porcionados ya sueles tener
/// (los ingredientes fijos, como salsas y condimentos, pesan menos que los componentes del plato).
/// </summary>
public sealed class PantryCookability(
    IRecipeRepository recipes,
    IPlanRepository plans,
    IIngredientRepository ingredients,
    AvailableIngredients available,
    PortionCalculator calculator)
{
    private const double FixedWeight = 0.3;

    public async Task<List<CookableRecipe>> RankAsync(int limit = 20, CancellationToken ct = default)
    {
        var plan = await plans.GetActiveAsync(ct) ?? throw new NotFoundException("No hay un plan de alimentación activo.");
        var catalog = (await ingredients.ListAsync(ct)).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var have = await available.LoadAsync(ct);

        var ranked = new List<CookableRecipe>();
        foreach (var recipe in await recipes.ListAsync(Meals.Comida, ct))
        {
            PortionedRecipe portioned;
            try
            {
                portioned = calculator.Calculate(recipe, plan, catalog);
            }
            catch (ConversionException)
            {
                continue;
            }

            if (!portioned.Compatible)
            {
                continue;
            }

            ranked.Add(Score(portioned, have));
        }

        return ranked
            .OrderByDescending(r => r.Coverage)
            .ThenBy(r => r.Missing.Count)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    public static CookableRecipe Score(PortionedRecipe portioned, IReadOnlyDictionary<string, AvailableIngredient> have)
    {
        double total = 0;
        double covered = 0;
        var haveNames = new List<string>();
        var missing = new List<string>();
        foreach (var line in portioned.Batch.GroupBy(l => l.IngredientId).Select(g => g.First()))
        {
            var weight = line.Scaled ? 1 : FixedWeight;
            total += weight;
            if (have.ContainsKey(line.IngredientId))
            {
                covered += weight;
                haveNames.Add(line.Name);
            }
            else
            {
                missing.Add(line.Name);
            }
        }

        return new CookableRecipe(
            portioned.RecipeId,
            portioned.Name,
            total == 0 ? 0 : Math.Round(covered / total, 2),
            haveNames,
            missing,
            portioned.Nutrition?.PerServing.Kcal);
    }
}
