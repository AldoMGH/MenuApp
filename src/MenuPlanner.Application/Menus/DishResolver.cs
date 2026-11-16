using MenuPlanner.Domain;
using MenuPlanner.Importers;

namespace MenuPlanner.Application.Menus;

public sealed record RecipeOption(string RecipeId, string Name);

public sealed record DishResolution(string Query, RecipeOption? Recipe, IReadOnlyList<RecipeOption> Candidates, string? Message)
{
    public bool Found => Recipe is not null;
}

/// <summary>
/// Traduce lo que escribes ("las enchiladas", "tinga", "Poyo ajonjolí", "lomo-salsa-verde-nopales") a una receta:
/// 1) id o nombre exacto de receta, 2) diccionario de platillos de Keep → receta ligada,
/// 3) palabras contenidas en el nombre de la receta, 4) similitud. Si hay varias opciones, las devuelve.
/// </summary>
public sealed class DishResolver(IRecipeRepository recipes, IDishRepository dishes, ISettingsRepository settings)
{
    private const double SimilarityThreshold = 0.75;

    public async Task<DishResolution> ResolveAsync(string query, IReadOnlyCollection<string>? within = null, CancellationToken ct = default)
    {
        var recipeList = (await recipes.ListAsync(Meals.Comida, ct))
            .Where(r => within is null || within.Contains(r.Id))
            .ToList();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DishResolution(query, null, [], "Indica el nombre del platillo.");
        }

        var key = Strip(TextNormalizer.Key(query));
        var options = recipeList.Select(r => new RecipeOption(r.Id, r.Name)).ToList();

        var exact = recipeList.FirstOrDefault(r =>
            string.Equals(r.Id, query.Trim(), StringComparison.OrdinalIgnoreCase) ||
            TextNormalizer.Key(r.Name) == key);
        if (exact is not null)
        {
            return Found(query, exact);
        }

        // Diccionario de tus notas de Keep: "poyo ajonjoli", "enchiladas", "bongles"…
        var dishList = await dishes.ListAsync(ct);
        var match = new DishMatcher(dishList, await settings.GetImportRulesAsync(ct)).Match(query);
        var fuzzyDish = match.Status == MatchStatus.Fuzzy ? match : null;
        if (match.DishId is not null && match.Status == MatchStatus.Matched)
        {
            var dish = dishList.First(d => d.Id == match.DishId);
            var linked = dish.RecipeId is null ? null : recipeList.FirstOrDefault(r => r.Id == dish.RecipeId);
            if (linked is not null)
            {
                return Found(query, linked);
            }

            if (within is null)
            {
                return new DishResolution(query, null, [],
                    $"'{dish.Name}' está en tu historial pero todavía no tiene receta ligada"
                    + (dish.Compatibility == DishCompatibility.NoCompatible ? " (no es compatible con el plan)." : "."));
            }
        }

        var tokens = TextNormalizer.MeaningfulTokens(key);
        if (tokens.Length > 0)
        {
            var contained = recipeList
                .Where(r =>
                {
                    var recipeTokens = TextNormalizer.Tokens(TextNormalizer.Key(r.Name + " " + r.Id.Replace('-', ' '))).ToHashSet(StringComparer.Ordinal);
                    return tokens.All(recipeTokens.Contains);
                })
                .ToList();
            if (contained.Count == 1)
            {
                return Found(query, contained[0]);
            }

            if (contained.Count > 1)
            {
                return new DishResolution(query, null, contained.Select(r => new RecipeOption(r.Id, r.Name)).ToList(),
                    $"'{query}' coincide con varias recetas; elige una.");
            }
        }

        if (fuzzyDish is not null &&
            dishList.First(d => d.Id == fuzzyDish.DishId).RecipeId is { } fuzzyRecipeId &&
            recipeList.FirstOrDefault(r => r.Id == fuzzyRecipeId) is { } fuzzyRecipe)
        {
            return Found(query, fuzzyRecipe);
        }

        var ranked = options
            .Select(o => (Option: o, Score: Math.Max(
                TextNormalizer.Similarity(key, TextNormalizer.Key(o.Name)),
                TextNormalizer.Similarity(key, o.RecipeId.Replace('-', ' ')))))
            .OrderByDescending(x => x.Score)
            .ToList();
        if (ranked.Count > 0 && ranked[0].Score >= SimilarityThreshold)
        {
            return new DishResolution(query, ranked[0].Option, [], null);
        }

        return new DishResolution(query, null, ranked.Take(3).Select(x => x.Option).ToList(),
            within is null ? $"No encontré una receta para '{query}'." : $"'{query}' no está en el menú.");
    }

    private static DishResolution Found(string query, Recipe recipe) => new(query, new RecipeOption(recipe.Id, recipe.Name), [], null);

    // "las enchiladas", "el alambre" → sin artículo inicial.
    private static string Strip(string key)
    {
        foreach (var article in new[] { "el ", "la ", "los ", "las ", "un ", "una " })
        {
            if (key.StartsWith(article, StringComparison.Ordinal))
            {
                return key[article.Length..];
            }
        }

        return key;
    }
}
