using System.Text.Json;
using System.Text.Json.Nodes;
using MenuPlanner.Application.Catalog;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;
using MenuPlanner.Importers;
using MenuPlanner.Portioning;

namespace MenuPlanner.Application.Suggestions;

public sealed class RecipeDraftResult
{
    public bool Valid => Errors.Count == 0 && Recipe is not null && Portioned is not null;

    public List<string> Errors { get; } = [];

    public Recipe? Recipe { get; set; }

    public PortionedRecipe? Portioned { get; set; }

    public CookableRecipe? Availability { get; set; }
}

public sealed record SavedDraft(Recipe Recipe, Dish Dish, MenuView? Menu);

/// <summary>
/// Recetas propuestas por fuera (Claude Desktop vía MCP): se validan con el mismo JSON Schema y las reglas del catálogo,
/// se porcionan con el plan y, si el usuario acepta, se guardan marcadas para revisión. El LLM propone; el código calcula.
/// </summary>
public sealed class RecipeDraftService(
    SchemaValidator validator,
    IRecipeRepository recipes,
    IIngredientRepository ingredients,
    IPlanRepository plans,
    IDishRepository dishes,
    AvailableIngredients available,
    PortionCalculator calculator,
    MenuService menus)
{
    public const string Source = "ia-sugerida";
    public const string ReviewTag = "revisar-nutriologa";

    public async Task<RecipeDraftResult> PreviewAsync(string recipeJson, CancellationToken ct = default)
    {
        var result = new RecipeDraftResult();
        JsonObject? node;
        try
        {
            node = JsonNode.Parse(recipeJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"El JSON no es válido: {ex.Message}");
            return result;
        }

        if (node is null)
        {
            result.Errors.Add("Se esperaba un objeto JSON con la receta.");
            return result;
        }

        // Valores que la app controla: id derivado del nombre si falta, comida de 4 porciones, origen y estadísticas.
        if (node["id"] is null && node["name"]?.GetValue<string>() is { } name)
        {
            node["id"] = TextNormalizer.Slug(name);
        }

        node["meal"] ??= Meals.Comida;
        node["servings"] ??= 4;
        node.Remove("stats");
        node["source"] = Source;

        result.Errors.AddRange(validator.Validate(SchemaNames.Recipe, node));
        if (result.Errors.Count > 0)
        {
            return result;
        }

        var recipe = node.Deserialize<Recipe>(AppJson.Options)!;
        var catalog = (await ingredients.ListAsync(ct)).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var plan = await plans.GetActiveAsync(ct) ?? throw new NotFoundException("No hay un plan de alimentación activo.");

        foreach (var error in CatalogValidator.ValidateRecipe(recipe, catalog, plan))
        {
            result.Errors.Add(WithHint(error, catalog));
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        try
        {
            var portioned = calculator.Calculate(recipe, plan, catalog, new PortionOptions { LegumeSideServings = recipe.Servings });
            if (!portioned.Compatible)
            {
                result.Errors.AddRange(portioned.Warnings);
            }

            result.Recipe = recipe;
            result.Portioned = portioned;
            result.Availability = PantryCookability.Score(portioned, await available.LoadAsync(ct));
        }
        catch (ConversionException ex)
        {
            result.Errors.Add($"{ex.Message} Usa una unidad que tenga equivalencia en el catálogo (ver get_suggestion_context).");
        }

        return result;
    }

    public async Task<(RecipeDraftResult Draft, SavedDraft? Saved)> SaveAsync(string recipeJson, bool addToMenu, string? menuId, CancellationToken ct = default)
    {
        var draft = await PreviewAsync(recipeJson, ct);
        if (!draft.Valid)
        {
            return (draft, null);
        }

        var recipe = draft.Recipe!;
        recipe.Id = await UniqueRecipeIdAsync(recipe.Id, ct);
        recipe.Source = Source;
        recipe.Stats = new RecipeStats();
        foreach (var tag in new[] { ReviewTag, "nueva" })
        {
            if (!recipe.Tags.Contains(tag))
            {
                recipe.Tags.Add(tag);
            }
        }

        await recipes.UpsertAsync(recipe, ct);

        var existingDishes = (await dishes.ListAsync(ct)).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var dishId = recipe.Id;
        for (var i = 2; existingDishes.Contains(dishId); i++)
        {
            dishId = $"{recipe.Id}-{i}";
        }

        var dish = new Dish
        {
            Id = dishId,
            Name = recipe.Name,
            Aliases = [TextNormalizer.Key(recipe.Name)],
            RecipeId = recipe.Id,
            Compatibility = DishCompatibility.Adaptada,
            Notes = "Propuesta por Claude con lo que sueles tener; revísala con tu nutrióloga.",
        };
        await dishes.UpsertAsync(dish, ct);

        MenuView? menu = null;
        if (addToMenu)
        {
            var id = menuId ?? (await menus.ListAsync(50, ct)).FirstOrDefault(m => m.Status == MenuStatus.Draft)?.Id
                ?? throw new BadRequestException("La receta se guardó, pero no hay un menú en borrador para agregarla.");
            menu = await menus.AddDishAsync(id, recipe.Id, ct);
        }

        return (draft, new SavedDraft(recipe, dish, menu));
    }

    private async Task<string> UniqueRecipeIdAsync(string id, CancellationToken ct)
    {
        var candidate = id;
        for (var i = 2; await recipes.GetAsync(candidate, ct) is not null; i++)
        {
            candidate = $"{id}-{i}";
        }

        return candidate;
    }

    /// <summary>Para ingredientes inexistentes, sugiere los ids más parecidos del catálogo.</summary>
    private static string WithHint(string error, IReadOnlyDictionary<string, Ingredient> catalog)
    {
        var start = error.IndexOf("'", StringComparison.Ordinal);
        var end = start < 0 ? -1 : error.IndexOf("'", start + 1, StringComparison.Ordinal);
        if (start < 0 || end < 0 || !error.Contains("no existe", StringComparison.Ordinal))
        {
            return error;
        }

        var unknown = error[(start + 1)..end];
        var key = TextNormalizer.Key(unknown.Replace('-', ' '));
        var closest = catalog.Values
            .Select(i => (i.Id, Score: Math.Max(TextNormalizer.Similarity(key, i.Id.Replace('-', ' ')), TextNormalizer.Similarity(key, TextNormalizer.Key(i.Name)))))
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Id);
        return $"{error} ¿Quisiste decir: {string.Join(", ", closest)}?";
    }
}
