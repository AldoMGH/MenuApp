using System.ComponentModel;
using System.Globalization;
using System.Text;
using MenuPlanner.Application;
using MenuPlanner.Application.Imports;
using MenuPlanner.Application.Menus;
using MenuPlanner.Domain;
using MenuPlanner.Portioning;
using ModelContextProtocol.Server;

namespace MenuPlanner.Mcp.Tools;

/// <summary>Recetas porcionadas, historial de Keep y despensa.</summary>
[McpServerToolType]
public sealed class RecipeTools(MenuService menus, DishResolver resolver, IRecipeRepository recipes, DishService dishes, ImportService imports)
{
    [McpServerTool(Name = "get_recipe", Title = "Receta porcionada", ReadOnly = true)]
    [Description("Receta ajustada al plan: cantidades para cocinar el lote de 4 porciones, la porción a servir y los pasos.")]
    public Task<string> GetRecipe(
        [Description("Platillo, como lo escribe el usuario (p. ej. 'pollo teriyaki', 'tinga').")] string dish,
        [Description("Punto del rango de proteína del plan: 'min', 'mid' o 'max'. Opcional (por defecto el del plan).")] string? proteinTarget = null,
        CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            ProteinTarget? target = null;
            if (!string.IsNullOrWhiteSpace(proteinTarget))
            {
                target = Enum.TryParse<ProteinTarget>(proteinTarget, ignoreCase: true, out var parsed)
                    ? parsed
                    : throw new BadRequestException("proteinTarget debe ser min, mid o max.");
            }

            var resolution = await resolver.ResolveAsync(dish, ct: ct);
            if (resolution.Recipe is null)
            {
                return MenuTools.Explain(resolution);
            }

            var portioned = await menus.PortionRecipeAsync(resolution.Recipe.RecipeId, target, null, ct);
            var recipe = await recipes.GetAsync(resolution.Recipe.RecipeId, ct);
            return FormatRecipe(portioned, recipe);
        });

    [McpServerTool(Name = "list_recipes", Title = "Recetas disponibles", ReadOnly = true)]
    [Description("Lista las recetas de comida del catálogo con su proteína, etiquetas y cuántas veces las has cocinado. Filtro opcional por texto o etiqueta.")]
    public Task<string> ListRecipes([Description("Texto o etiqueta a buscar (p. ej. 'pollo', 'tacos', 'revisar-nutriologa'). Opcional.")] string? filter = null, CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var list = await recipes.ListAsync(Meals.Comida, ct);
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var key = Importers.TextNormalizer.Key(filter);
                list = list.Where(r =>
                        Importers.TextNormalizer.Key(r.Name).Contains(key, StringComparison.Ordinal) ||
                        r.Tags.Any(t => Importers.TextNormalizer.Key(t) == key))
                    .ToList();
            }

            if (list.Count == 0)
            {
                return "No hay recetas con ese filtro.";
            }

            var sb = new StringBuilder();
            foreach (var recipe in list.OrderByDescending(r => r.Stats.TotalCount).ThenBy(r => r.Name, StringComparer.CurrentCulture))
            {
                var tags = recipe.Tags.Count == 0 ? string.Empty : $" [{string.Join(", ", recipe.Tags)}]";
                sb.AppendLine($"- {recipe.Name} ({recipe.Id}) · cocinada {recipe.Stats.TotalCount} veces{tags}");
            }

            return sb.ToString();
        });

    [McpServerTool(Name = "list_favorites", Title = "Platillos favoritos", ReadOnly = true)]
    [Description("Los platillos que más cocina el usuario según su historial de Google Keep, con la última vez y si tienen receta ligada.")]
    public Task<string> ListFavorites([Description("Cuántos mostrar (por defecto 15).")] int top = 15, CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var list = (await dishes.ListAsync("frequency", ct)).Where(d => d.Dish.Stats.TimesCooked > 0).Take(Math.Clamp(top, 1, 100)).ToList();
            if (list.Count == 0)
            {
                return "Todavía no hay historial importado de Keep.";
            }

            var sb = new StringBuilder();
            foreach (var view in list)
            {
                var dish = view.Dish;
                var last = dish.Stats.LastCooked?.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("es-MX")) ?? "—";
                var recipe = view.RecipeName is null ? "sin receta" : $"receta: {view.RecipeName}";
                sb.AppendLine($"- {dish.Name}: {dish.Stats.TimesCooked} veces, última {last} ({recipe}; {Compatibility(dish.Compatibility)})");
            }

            return sb.ToString();
        });

    [McpServerTool(Name = "pantry_suggestions", Title = "Sugerencias de despensa", ReadOnly = true)]
    [Description("Ingredientes que el usuario compra con frecuencia según sus listas de Keep: candidatos para su despensa o básicos semanales.")]
    public Task<string> PantrySuggestions([Description("Meses hacia atrás a considerar (por defecto 6).")] int months = 6, CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var suggestions = await imports.GetPantrySuggestionsAsync(months, 0.3, ct);
            if (suggestions.Count == 0)
            {
                return "No hay historial de compras importado.";
            }

            var sb = new StringBuilder();
            foreach (var s in suggestions.Take(25))
            {
                var qty = s.TypicalQty is null || s.TypicalUnit is null ? string.Empty : $", normalmente {QuantityFormatter.Format(s.TypicalQty.Value, s.TypicalUnit.Value)}";
                var flags = s.InPantry ? " (ya en despensa)" : s.InBasics ? " (ya en básicos)" : string.Empty;
                sb.AppendLine($"- {s.Name}: {s.WeeksBought} de {s.WeeksInWindow} semanas{qty}{flags}");
            }

            return sb.ToString();
        });

    internal static string FormatRecipe(PortionedRecipe portioned, Recipe? recipe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{portioned.Name} ({portioned.RecipeId}) · {portioned.Servings} porciones · plan v{portioned.PlanVersion}");
        if (recipe?.Tags.Contains("revisar-nutriologa") == true)
        {
            sb.AppendLine("Versión adaptada al plan: conviene revisarla con la nutrióloga.");
        }

        sb.AppendLine();
        sb.AppendLine("Para cocinar (lote):");
        foreach (var line in portioned.Batch)
        {
            sb.AppendLine($"- {line.Display} {line.Name.ToLowerInvariant()}{(line.Suggested ? " (acompañamiento sugerido)" : string.Empty)}");
        }

        sb.AppendLine();
        sb.AppendLine("Tu porción al servir:");
        foreach (var line in portioned.Serving)
        {
            sb.AppendLine($"- {line.Text}");
        }

        if (portioned.Nutrition is not null)
        {
            sb.AppendLine(SuggestionTools.Macros(portioned.Nutrition));
        }

        if (portioned.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Pasos:");
            for (var i = 0; i < portioned.Steps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {portioned.Steps[i]}");
            }
        }

        foreach (var text in portioned.Suggestions.Concat(portioned.Warnings))
        {
            sb.AppendLine($"Nota: {text}");
        }

        return sb.ToString();
    }

    private static string Compatibility(DishCompatibility compatibility) => compatibility switch
    {
        DishCompatibility.Adaptada => "versión adaptada",
        DishCompatibility.NoCompatible => "no compatible con el plan",
        _ => "compatible",
    };
}
