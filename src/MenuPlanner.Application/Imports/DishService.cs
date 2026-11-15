using MenuPlanner.Domain;

namespace MenuPlanner.Application.Imports;

public sealed record DishView(Dish Dish, string? RecipeName);

/// <summary>Catálogo de platillos de tu historial: consulta y edición (alias, receta ligada, compatibilidad).</summary>
public sealed class DishService(IDishRepository dishes, IRecipeRepository recipes, ImportService imports)
{
    public async Task<List<DishView>> ListAsync(string? sort, CancellationToken ct = default)
    {
        var list = await dishes.ListAsync(ct);
        var recipeNames = (await recipes.ListAsync(ct: ct)).ToDictionary(r => r.Id, r => r.Name, StringComparer.Ordinal);
        IEnumerable<Dish> ordered = string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase)
            ? list.OrderBy(d => d.Name, StringComparer.CurrentCulture)
            : list.OrderByDescending(d => d.Stats.TimesCooked).ThenBy(d => d.Name, StringComparer.CurrentCulture);
        return ordered
            .Select(d => new DishView(d, d.RecipeId is null ? null : recipeNames.GetValueOrDefault(d.RecipeId)))
            .ToList();
    }

    public async Task<Dish> UpdateAsync(Dish dish, CancellationToken ct = default)
    {
        if (dish.RecipeId is not null && await recipes.GetAsync(dish.RecipeId, ct) is null)
        {
            throw new BadRequestException($"No existe la receta '{dish.RecipeId}'.");
        }

        if (await dishes.GetAsync(dish.Id, ct) is { } existing)
        {
            dish.Stats = existing.Stats;
        }

        await dishes.UpsertAsync(dish, ct);
        await imports.RematchAllAsync(ct);
        return await dishes.GetAsync(dish.Id, ct) ?? dish;
    }
}
