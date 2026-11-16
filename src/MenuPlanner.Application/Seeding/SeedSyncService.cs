using MenuPlanner.Application.Imports;
using MenuPlanner.Domain;

namespace MenuPlanner.Application.Seeding;

/// <summary>
/// Trae a una base existente lo nuevo del seed sin pisar tus datos: agrega ingredientes, platillos y planes que falten,
/// actualiza las recetas de origen seed (conservando sus estadísticas) y quita las recetas de arranque inventadas ("semilla").
/// </summary>
public sealed class SeedSyncService(
    IIngredientRepository ingredients,
    IRecipeRepository recipes,
    IDishRepository dishes,
    IPlanRepository plans,
    ISettingsRepository settings,
    ImportService imports)
{
    public const string RemovedSource = "semilla";

    public async Task<SeedSyncResult> SyncAsync(SeedData source, CancellationToken ct = default)
    {
        var seed = new SeedData
        {
            Plans = source.Plans.Select(AppJson.Clone).ToList(),
            Ingredients = source.Ingredients.Select(AppJson.Clone).ToList(),
            Recipes = source.Recipes.Select(AppJson.Clone).ToList(),
            Dishes = source.Dishes.Select(AppJson.Clone).ToList(),
            ImportRules = AppJson.Clone(source.ImportRules),
        };

        var existingIngredients = (await ingredients.ListAsync(ct)).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var newIngredients = seed.Ingredients.Where(i => !existingIngredients.Contains(i.Id)).ToList();
        await ingredients.UpsertManyAsync(newIngredients, ct);

        var currentRecipes = (await recipes.ListAsync(ct: ct)).ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var recipe in seed.Recipes)
        {
            if (currentRecipes.TryGetValue(recipe.Id, out var existing))
            {
                recipe.Stats = existing.Stats;
            }
        }

        await recipes.UpsertManyAsync(seed.Recipes, ct);
        var seedIds = seed.Recipes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var recipe in currentRecipes.Values.Where(r => r.Source == RemovedSource && !seedIds.Contains(r.Id)))
        {
            removed += await recipes.DeleteAsync(recipe.Id, ct) ? 1 : 0;
        }

        var currentDishes = (await dishes.ListAsync(ct)).ToDictionary(d => d.Id, StringComparer.Ordinal);
        var dishesToSave = new List<Dish>();
        var added = 0;
        var updated = 0;
        foreach (var dish in seed.Dishes)
        {
            if (!currentDishes.TryGetValue(dish.Id, out var existing))
            {
                dishesToSave.Add(dish);
                added++;
                continue;
            }

            var newAliases = dish.Aliases.Except(existing.Aliases, StringComparer.Ordinal).ToList();
            var linkRecipe = existing.RecipeId is null && dish.RecipeId is not null;
            if (newAliases.Count > 0 || linkRecipe)
            {
                existing.Aliases.AddRange(newAliases);
                existing.RecipeId ??= dish.RecipeId;
                dishesToSave.Add(existing);
                updated++;
            }
        }

        await dishes.UpsertManyAsync(dishesToSave, ct);

        var rules = await settings.GetImportRulesAsync(ct);
        var rulesAdded = 0;
        foreach (var fix in seed.ImportRules.SpellingFixes.Where(f => !rules.SpellingFixes.Any(r => r.From == f.From)))
        {
            rules.SpellingFixes.Add(fix);
            rulesAdded++;
        }

        foreach (var ignore in seed.ImportRules.Ignore.Where(i => !rules.Ignore.Contains(i)))
        {
            rules.Ignore.Add(ignore);
            rulesAdded++;
        }

        await settings.SaveImportRulesAsync(rules, ct);

        var existingPlans = (await plans.ListAsync(ct)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var plansAdded = 0;
        foreach (var plan in seed.Plans.Where(p => !existingPlans.Contains(p.Id)))
        {
            plan.Active = existingPlans.Count == 0 && plan.Active;
            await plans.UpsertAsync(plan, ct);
            plansAdded++;
        }

        await imports.RematchAllAsync(ct);
        return new SeedSyncResult(newIngredients.Count, seed.Recipes.Count, removed, added, updated, rulesAdded, plansAdded);
    }
}
