using System.Globalization;
using System.Text;
using MenuPlanner.Domain;

namespace MenuPlanner.Application.Suggestions;

/// <summary>
/// Arma el contexto que Claude necesita para proponer un platillo nuevo que cumpla el plan: plantilla del plato,
/// intercambios, reglas, lo que sueles tener (con ids), el catálogo, tus favoritos y el formato JSON esperado.
/// </summary>
public sealed class SuggestionContextBuilder(
    IPlanRepository plans,
    IIngredientRepository ingredients,
    IRecipeRepository recipes,
    IDishRepository dishes,
    AvailableIngredients available)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<string> BuildAsync(string? focus, IReadOnlyCollection<string>? avoid, CancellationToken ct = default)
    {
        var plan = await plans.GetActiveAsync(ct) ?? throw new NotFoundException("No hay un plan de alimentación activo.");
        var catalog = (await ingredients.ListAsync(ct)).OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
        var have = await available.LoadAsync(ct);
        var avoidSet = new HashSet<string>(avoid ?? [], StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(focus))
        {
            sb.AppendLine($"# Lo que pidió el usuario: {focus.Trim()}");
            sb.AppendLine();
        }

        var comida = plan.GetMeal(Meals.Comida);
        sb.AppendLine($"# Plan activo: {plan.Name} (v{plan.Version}). Plato de la comida, por porción");
        foreach (var slot in comida?.Components ?? [])
        {
            var range = slot.Min == slot.Max ? N(slot.Max) : $"{N(slot.Min)}–{N(slot.Max)}";
            var extra = new List<string>();
            if (slot.State == FoodState.Cocido)
            {
                extra.Add("medido cocido");
            }

            if (slot.Optional)
            {
                extra.Add("opcional");
            }

            if (slot.WeeklyFrequency is { } f)
            {
                extra.Add($"{f.Min}–{f.Max} veces por semana; en la receta puede ir incluida o como acompañamiento opcional (\"optional\": true)");
            }

            if (slot.Constraint is not null)
            {
                extra.Add(slot.Constraint);
            }

            sb.AppendLine($"- {slot.Group.ToString().ToLowerInvariant()}: {range} {slot.Unit.ToString().ToLowerInvariant()}{(extra.Count > 0 ? $" ({string.Join("; ", extra)})" : string.Empty)}");
        }

        foreach (var exchange in plan.Exchanges)
        {
            sb.AppendLine($"- 1 porción de {exchange.Group.ToString().ToLowerInvariant()} = " +
                string.Join(" | ", exchange.Options.Select(o => $"{o.IngredientId} ({N(o.Qty)} {o.Unit.ToString().ToLowerInvariant()}{(o.State == FoodState.Cocido ? " cocido" : string.Empty)})")));
        }

        sb.AppendLine($"Reglas: métodos de cocción prohibidos: {string.Join(", ", plan.Rules.ForbiddenMethods)}. Proteína magra. {string.Join(" ", plan.Rules.Notes)}");
        sb.AppendLine("La app calcula las cantidades de los componentes con el plan: en \"components\" NO pongas cantidades, solo ingredientes y \"share\" (reparto).");
        sb.AppendLine("Los componentes cereal y grasa solo aceptan ingredientes de la tabla de intercambios. Las verduras deben tener equivalencia en tazas (tz).");
        sb.AppendLine();

        sb.AppendLine("# Lo que el usuario suele tener (prefiere estos ingredientes)");
        foreach (var ingredient in catalog.Where(i => have.ContainsKey(i.Id) && !Avoided(i, avoidSet)).OrderByDescending(i => have[i.Id].Share))
        {
            sb.AppendLine($"- {Describe(ingredient)} · {have[ingredient.Id].Source}");
        }

        if (have.Count == 0)
        {
            sb.AppendLine("- (sin datos: no hay despensa, básicos ni historial de compras; usa el catálogo)");
        }

        sb.AppendLine();
        sb.AppendLine("# Resto del catálogo (id: nombre [grupo] unidades con equivalencia)");
        foreach (var ingredient in catalog.Where(i => !have.ContainsKey(i.Id) && !Avoided(i, avoidSet)))
        {
            sb.AppendLine($"- {Describe(ingredient)}");
        }

        if (avoidSet.Count > 0)
        {
            sb.AppendLine($"Evitar: {string.Join(", ", avoidSet)}.");
        }

        var favorites = (await dishes.ListAsync(ct)).Where(d => d.Stats.TimesCooked > 0).OrderByDescending(d => d.Stats.TimesCooked).Take(12).ToList();
        var existing = await recipes.ListAsync(Meals.Comida, ct);
        sb.AppendLine();
        sb.AppendLine("# Gustos del usuario");
        sb.AppendLine(favorites.Count == 0
            ? "- (sin historial importado)"
            : $"- Más cocinados: {string.Join(", ", favorites.Select(d => $"{d.Name} ({d.Stats.TimesCooked})"))}");
        sb.AppendLine($"- Métodos que usa: {string.Join(", ", existing.Select(r => r.CookingMethod).Distinct().Order())}");
        sb.AppendLine($"- Recetas que ya existen (propón algo distinto): {string.Join("; ", existing.Select(r => r.Name))}");
        sb.AppendLine();

        sb.AppendLine("# Formato (JSON de recipe.schema.json). Ejemplo:");
        sb.AppendLine(Example);
        sb.AppendLine("Unidades válidas en fixedIngredients: g, ml, pieza, taza, cda, cdita, diente, paquete, lata (con equivalencia en el ingrediente).");
        sb.AppendLine("Pasos en español, breves. Llama a preview_recipe con el JSON; corrige lo que indique; muestra el resultado al usuario y solo si acepta usa save_recipe.");
        return sb.ToString();
    }

    private const string Example = """
        {
          "name": "Pollo al chipotle con calabacitas y arroz",
          "cookingMethod": "guisado",
          "tags": ["pollo"],
          "components": [
            { "group": "proteina", "ingredients": [ { "ingredientId": "pechuga-pollo" } ] },
            { "group": "verdura", "ingredients": [ { "ingredientId": "calabacita", "share": 0.6 }, { "ingredientId": "cebolla", "share": 0.4 } ] },
            { "group": "leguminosa", "ingredients": [ { "ingredientId": "frijol" } ], "optional": true },
            { "group": "cereal", "ingredients": [ { "ingredientId": "arroz" } ] }
          ],
          "fixedIngredients": [
            { "ingredientId": "jitomate", "qty": 300, "unit": "g", "note": "salsa" },
            { "ingredientId": "chile-chipotle", "qty": 2, "unit": "pieza" },
            { "ingredientId": "ajo", "qty": 2, "unit": "diente" }
          ],
          "steps": ["Cuece y deshebra el pollo.", "Licúa jitomate, chipotle y ajo; guisa con el pollo y las verduras."]
        }
        """;

    private static bool Avoided(Ingredient ingredient, HashSet<string> avoid) =>
        avoid.Contains(ingredient.Id) || avoid.Any(a => ingredient.Name.Contains(a, StringComparison.CurrentCultureIgnoreCase));

    private static string Describe(Ingredient i)
    {
        var units = new List<string> { i.BaseUnit.ToString().ToLowerInvariant() };
        units.AddRange(i.Conversions.Select(c => c.Unit.ToString().ToLowerInvariant() + (c.State == FoodState.Cocido ? " cocido" : string.Empty)));
        var group = i.Group is null ? string.Empty : $" [{i.Group.ToString()!.ToLowerInvariant()}]";
        return $"{i.Id}: {i.Name}{group} ({string.Join(", ", units.Distinct())}){(i.Group == ComponentGroup.Proteina && !i.IsLean ? " NO magra" : string.Empty)}";
    }

    private static string N(double value) => value.ToString("0.##", Inv);
}
