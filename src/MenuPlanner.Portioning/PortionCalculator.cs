using System.Globalization;
using MenuPlanner.Domain;

namespace MenuPlanner.Portioning;

public sealed class PortionOptions
{
    /// <summary>Porciones del lote que llevan la leguminosa de acompañamiento (solo recetas con leguminosa opcional).</summary>
    public int LegumeSideServings { get; init; }

    /// <summary>Si se indica, sustituye el punto del rango de proteína definido en el plan.</summary>
    public ProteinTarget? ProteinTarget { get; init; }
}

/// <summary>
/// Motor determinista de porciones: toma la plantilla del tiempo de comida del plan y calcula, para cada componente
/// de la receta, cuánto comprar/cocinar para el lote (crudo) y cuánto servir por plato.
/// <c>cantidad lote = porción del plan × porciones ÷ rendimiento crudo→cocido</c>.
/// </summary>
public sealed class PortionCalculator(UnitConverter converter)
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public PortionedRecipe Calculate(
        Recipe recipe,
        NutritionPlan plan,
        IReadOnlyDictionary<string, Ingredient> ingredients,
        PortionOptions? options = null)
    {
        options ??= new PortionOptions();
        var template = plan.GetMeal(recipe.Meal)
            ?? throw new ConversionException($"El plan '{plan.Id}' no define el tiempo de comida '{recipe.Meal}'.");

        var servings = recipe.Servings > 0 ? recipe.Servings : 4;
        var result = new PortionedRecipe
        {
            RecipeId = recipe.Id,
            Name = recipe.Name,
            Servings = servings,
            PlanId = plan.Id,
            PlanVersion = plan.Version,
            Steps = [.. recipe.Steps],
        };

        if (plan.Rules.ForbiddenMethods.Any(m => string.Equals(m, recipe.CookingMethod, StringComparison.OrdinalIgnoreCase)))
        {
            result.Compatible = false;
            result.Warnings.Add($"El método de cocción '{recipe.CookingMethod}' no está permitido por el plan.");
        }

        var target = options.ProteinTarget ?? plan.ProteinTarget;

        foreach (var slot in template.Components)
        {
            var component = recipe.GetComponent(slot.Group);
            if (component is null || component.Ingredients.Count == 0)
            {
                AddMissingComponent(result, slot, plan, ingredients, servings);
                continue;
            }

            var componentServings = slot.Group == ComponentGroup.Leguminosa && component.Optional
                ? Math.Clamp(options.LegumeSideServings, 0, servings)
                : servings;

            if (componentServings == 0)
            {
                continue;
            }

            var amount = PickAmount(slot, target);
            if (slot.Group == ComponentGroup.Proteina)
            {
                result.ProteinPerServingGrams = amount;
            }

            AddComponentLines(result, slot, component.Ingredients, amount, componentServings, plan, ingredients, suggested: false);

            if (slot.Group == ComponentGroup.Proteina)
            {
                foreach (var item in component.Ingredients)
                {
                    if (ingredients.TryGetValue(item.IngredientId, out var protein) && !protein.IsLean)
                    {
                        result.Warnings.Add($"'{protein.Name}' no es una proteína magra; el plan pide priorizar proteínas sin grasa.");
                    }
                }
            }
        }

        foreach (var fixedItem in recipe.FixedIngredients)
        {
            var ingredient = Require(ingredients, fixedItem.IngredientId, recipe.Name);
            var qty = QuantityFormatter.RoundForCooking(
                converter.ToBase(ingredient, fixedItem.Qty, fixedItem.Unit, FoodState.Crudo),
                ingredient.BaseUnit);

            result.Batch.Add(new PortionLine
            {
                IngredientId = ingredient.Id,
                Name = ingredient.Name,
                Group = null,
                Quantity = qty,
                Unit = ingredient.BaseUnit,
                Display = QuantityFormatter.Format(qty, ingredient.BaseUnit),
                Scaled = false,
                Servings = servings,
            });
        }

        return result;
    }

    /// <summary>Proteína usa el punto del rango elegido; el resto de componentes usa el máximo (si la receta lo incluye, se sirve completo).</summary>
    public static double PickAmount(ComponentSlot slot, ProteinTarget target)
    {
        if (slot.Group != ComponentGroup.Proteina)
        {
            return slot.Max;
        }

        return target switch
        {
            ProteinTarget.Min => slot.Min,
            ProteinTarget.Mid => (slot.Min + slot.Max) / 2,
            _ => slot.Max,
        };
    }

    private void AddMissingComponent(
        PortionedRecipe result,
        ComponentSlot slot,
        NutritionPlan plan,
        IReadOnlyDictionary<string, Ingredient> ingredients,
        int servings)
    {
        // Componentes semanales (leguminosa 2–3 v/sem) u opcionales (grasa) no son obligatorios en cada plato.
        if (slot.Optional || slot.WeeklyFrequency is not null)
        {
            return;
        }

        var groupName = GroupName(slot.Group);
        string? sideId = slot.Group switch
        {
            ComponentGroup.Verdura => plan.Rules.DefaultVegetableSideId,
            ComponentGroup.Proteina => null,
            _ => plan.Exchanges.FirstOrDefault(e => e.Group == slot.Group)?.Options.FirstOrDefault()?.IngredientId,
        };

        if (sideId is null || !ingredients.ContainsKey(sideId))
        {
            result.Warnings.Add($"La receta no incluye {groupName} y no hay un acompañamiento configurado para completarla.");
            return;
        }

        var amount = PickAmount(slot, plan.ProteinTarget);
        var before = result.Serving.Count;
        AddComponentLines(result, slot, [new ComponentIngredient { IngredientId = sideId }], amount, servings, plan, ingredients, suggested: true);
        var servingText = result.Serving.Count > before ? result.Serving[^1].Text : groupName;
        result.Suggestions.Add($"La receta no trae {groupName}: acompáñala con {servingText} por plato (ya está en la lista).");
    }

    private void AddComponentLines(
        PortionedRecipe result,
        ComponentSlot slot,
        IReadOnlyList<ComponentIngredient> items,
        double amountPerServing,
        int componentServings,
        NutritionPlan plan,
        IReadOnlyDictionary<string, Ingredient> ingredients,
        bool suggested)
    {
        var totalShare = items.Sum(i => i.Share > 0 ? i.Share : 1);
        var servingParts = new List<string>();

        foreach (var item in items)
        {
            var ingredient = Require(ingredients, item.IngredientId, result.Name);
            var share = (item.Share > 0 ? item.Share : 1) / totalShare;

            double qtyPerServing;
            Unit unit;
            FoodState state;
            if (slot.Unit == Unit.Porcion)
            {
                var option = plan.GetExchange(slot.Group, ingredient.Id);
                if (option is null)
                {
                    result.Warnings.Add(
                        $"'{ingredient.Name}' no está en la tabla de intercambios de {GroupName(slot.Group)} del plan; no se pudo porcionar.");
                    continue;
                }

                qtyPerServing = option.Qty * amountPerServing * share;
                unit = option.Unit;
                state = option.State;
            }
            else
            {
                qtyPerServing = amountPerServing * share;
                unit = slot.Unit;
                state = slot.State;
            }

            var baseQty = converter.ToBase(ingredient, qtyPerServing * componentServings, unit, state);
            var rounded = QuantityFormatter.RoundForCooking(baseQty, ingredient.BaseUnit);

            result.Batch.Add(new PortionLine
            {
                IngredientId = ingredient.Id,
                Name = ingredient.Name,
                Group = slot.Group,
                Quantity = rounded,
                Unit = ingredient.BaseUnit,
                Display = QuantityFormatter.Format(rounded, ingredient.BaseUnit),
                Scaled = true,
                Suggested = suggested,
                Servings = componentServings,
            });

            var stateText = state == FoodState.Cocido ? " (en cocido)" : string.Empty;
            servingParts.Add($"{QuantityFormatter.Format(Math.Round(qtyPerServing, 2), unit)} de {ingredient.Name.ToLower(Culture)}{stateText}");
        }

        if (servingParts.Count == 0)
        {
            return;
        }

        var text = string.Join(" + ", servingParts);
        if (componentServings < result.Servings)
        {
            text += $" (en {componentServings} de {result.Servings} porciones)";
        }

        result.Serving.Add(new ServingLine { Group = slot.Group, Text = text });
    }

    private static Ingredient Require(IReadOnlyDictionary<string, Ingredient> ingredients, string id, string recipeName) =>
        ingredients.TryGetValue(id, out var ingredient)
            ? ingredient
            : throw new ConversionException($"La receta '{recipeName}' usa el ingrediente '{id}', que no existe en el catálogo.");

    public static string GroupName(ComponentGroup group) => group switch
    {
        ComponentGroup.Verdura => "verdura",
        ComponentGroup.Proteina => "proteína",
        ComponentGroup.Leguminosa => "leguminosa",
        ComponentGroup.Cereal => "cereal o tubérculo",
        ComponentGroup.Grasa => "grasa saludable",
        _ => group.ToString().ToLowerInvariant(),
    };
}
