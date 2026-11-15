using MenuPlanner.Domain;

namespace MenuPlanner.Application.Catalog;

/// <summary>Reglas de integridad que JSON Schema no puede expresar (referencias entre documentos).</summary>
public static class CatalogValidator
{
    public static List<string> ValidateRecipe(Recipe recipe, IReadOnlyDictionary<string, Ingredient> ingredients, NutritionPlan? plan)
    {
        var errors = new List<string>();

        foreach (var group in recipe.Components.GroupBy(c => c.Group).Where(g => g.Count() > 1))
        {
            errors.Add($"El componente '{group.Key}' aparece más de una vez.");
        }

        foreach (var component in recipe.Components)
        {
            foreach (var item in component.Ingredients)
            {
                if (!ingredients.ContainsKey(item.IngredientId))
                {
                    errors.Add($"components[{component.Group}]: el ingrediente '{item.IngredientId}' no existe.");
                    continue;
                }

                var slot = plan?.GetMeal(recipe.Meal)?.GetSlot(component.Group);
                if (slot?.Unit == Unit.Porcion && plan!.GetExchange(component.Group, item.IngredientId) is null)
                {
                    errors.Add($"components[{component.Group}]: '{item.IngredientId}' no está en la tabla de intercambios del plan.");
                }
            }

            if (component.Optional && component.Group != ComponentGroup.Leguminosa)
            {
                errors.Add($"components[{component.Group}]: solo la leguminosa puede ser opcional.");
            }
        }

        foreach (var item in recipe.FixedIngredients)
        {
            if (!ingredients.ContainsKey(item.IngredientId))
            {
                errors.Add($"fixedIngredients: el ingrediente '{item.IngredientId}' no existe.");
            }
        }

        if (plan is not null && plan.GetMeal(recipe.Meal) is null)
        {
            errors.Add($"El plan activo no define el tiempo de comida '{recipe.Meal}'.");
        }

        return errors;
    }

    public static List<string> ValidatePlan(NutritionPlan plan, IReadOnlyDictionary<string, Ingredient> ingredients)
    {
        var errors = new List<string>();
        foreach (var exchange in plan.Exchanges)
        {
            foreach (var option in exchange.Options.Where(o => !ingredients.ContainsKey(o.IngredientId)))
            {
                errors.Add($"exchanges[{exchange.Group}]: el ingrediente '{option.IngredientId}' no existe.");
            }
        }

        foreach (var basic in plan.WeeklyBasics.Where(b => !ingredients.ContainsKey(b.IngredientId)))
        {
            errors.Add($"weeklyBasics: el ingrediente '{basic.IngredientId}' no existe.");
        }

        if (plan.Rules.DefaultVegetableSideId is { } side && !ingredients.ContainsKey(side))
        {
            errors.Add($"rules.defaultVegetableSideId: el ingrediente '{side}' no existe.");
        }

        return errors;
    }
}
