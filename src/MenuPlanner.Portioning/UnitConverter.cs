using MenuPlanner.Domain;

namespace MenuPlanner.Portioning;

public sealed class ConversionException(string message) : Exception(message);

/// <summary>
/// Traduce medidas del plan (½ tz de arroz cocido, 200 g de pollo cocido, 2 tortillas) a la unidad base cruda
/// del ingrediente, y de la unidad base a la unidad de compra. Usa gramos crudos como pivote.
/// </summary>
public sealed class UnitConverter
{
    /// <summary>Convierte una cantidad medida en <paramref name="unit"/>/<paramref name="state"/> a la unidad base (cruda) del ingrediente.</summary>
    public double ToBase(Ingredient ingredient, double qty, Unit unit, FoodState state)
    {
        if (unit == ingredient.BaseUnit)
        {
            return state == FoodState.Cocido && IsMass(unit) ? qty / Yield(ingredient) : qty;
        }

        var rawGrams = ToRawGrams(ingredient, qty, unit, state);
        return FromRawGrams(ingredient, rawGrams, ingredient.BaseUnit);
    }

    /// <summary>Convierte entre dos unidades de un ingrediente en estado crudo (p. ej. base → unidad de compra).</summary>
    public double Convert(Ingredient ingredient, double qty, Unit from, Unit to)
    {
        if (from == to)
        {
            return qty;
        }

        var rawGrams = ToRawGrams(ingredient, qty, from, FoodState.Crudo);
        return FromRawGrams(ingredient, rawGrams, to);
    }

    private static double ToRawGrams(Ingredient ingredient, double qty, Unit unit, FoodState state)
    {
        if (IsMass(unit))
        {
            return state == FoodState.Cocido ? qty / Yield(ingredient) : qty;
        }

        var exact = ingredient.Conversions.FirstOrDefault(c => c.Unit == unit && c.State == state);
        if (exact is not null)
        {
            var grams = qty * exact.Grams;
            return exact.State == FoodState.Cocido ? grams / Yield(ingredient) : grams;
        }

        // Unidades contables (½ papa cocida = ½ papa cruda): se conserva la cuenta y se usa el peso crudo.
        var raw = ingredient.Conversions.FirstOrDefault(c => c.Unit == unit && c.State == FoodState.Crudo);
        if (raw is not null)
        {
            return qty * raw.Grams;
        }

        throw new ConversionException(
            $"No hay conversión de '{unit}' ({state}) a gramos para '{ingredient.Name}' ({ingredient.Id}).");
    }

    private static double FromRawGrams(Ingredient ingredient, double rawGrams, Unit target)
    {
        if (IsMass(target))
        {
            return rawGrams;
        }

        var conversion = ingredient.Conversions.FirstOrDefault(c => c.Unit == target && c.State == FoodState.Crudo);
        if (conversion is null || conversion.Grams <= 0)
        {
            throw new ConversionException(
                $"No hay conversión de gramos a '{target}' para '{ingredient.Name}' ({ingredient.Id}).");
        }

        return rawGrams / conversion.Grams;
    }

    // Para ml se asume densidad ≈ 1 (yogurt, aceite): suficiente para porcionar y comprar.
    private static bool IsMass(Unit unit) => unit is Unit.G or Unit.Ml;

    private static double Yield(Ingredient ingredient) => ingredient.CookedYield > 0 ? ingredient.CookedYield : 1;
}
