namespace MenuPlanner.Domain;

/// <summary>
/// Plan de alimentación versionado. No se basa en macros: describe, por tiempo de comida, qué grupos de alimentos
/// lleva el plato y en qué cantidad (medidas caseras / gramos cocidos), más tablas de intercambio por grupo.
/// </summary>
public class NutritionPlan
{
    public string Id { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime? ValidFrom { get; set; }

    public bool Active { get; set; }

    public ProteinTarget ProteinTarget { get; set; } = ProteinTarget.Max;

    /// <summary>Comidas principales por semana que cubre el menú (para calcular cobertura y frecuencias).</summary>
    public int MealsPerWeek { get; set; } = 7;

    public List<MealTemplate> Meals { get; set; } = [];

    public List<ExchangeList> Exchanges { get; set; } = [];

    /// <summary>Básicos que el plan pide a diario (p. ej. colación) y que se suman a la lista de compras.</summary>
    public List<WeeklyBasic> WeeklyBasics { get; set; } = [];

    public PlanRules Rules { get; set; } = new();

    public MealTemplate? GetMeal(string meal) =>
        Meals.FirstOrDefault(m => string.Equals(m.Meal, meal, StringComparison.OrdinalIgnoreCase));

    public ExchangeOption? GetExchange(ComponentGroup group, string ingredientId) =>
        Exchanges.FirstOrDefault(e => e.Group == group)?.Options.FirstOrDefault(o => o.IngredientId == ingredientId);
}

public class MealTemplate
{
    public string Meal { get; set; } = Meals.Comida;

    public List<ComponentSlot> Components { get; set; } = [];

    public ComponentSlot? GetSlot(ComponentGroup group) => Components.FirstOrDefault(c => c.Group == group);
}

/// <summary>
/// Un componente del plato, p. ej. "150–200 g de proteína magra cocida" o "1 porción de cereal".
/// Si <see cref="Unit"/> es <see cref="Domain.Unit.Porcion"/>, la cantidad real sale de la tabla de intercambios.
/// </summary>
public class ComponentSlot
{
    public ComponentGroup Group { get; set; }

    public double Min { get; set; }

    public double Max { get; set; }

    public Unit Unit { get; set; }

    public FoodState State { get; set; } = FoodState.Crudo;

    public bool Optional { get; set; }

    /// <summary>Si existe, el componente no va en cada comida sino N veces por semana (leguminosa 2–3 v/sem).</summary>
    public WeeklyFrequency? WeeklyFrequency { get; set; }

    public string? Constraint { get; set; }

    public string? Notes { get; set; }
}

public class WeeklyFrequency
{
    public int Min { get; set; }

    public int Max { get; set; }
}

/// <summary>Opciones intercambiables de un grupo: 1 porción de cereal = 2 tortillas = ½ tz de arroz cocido…</summary>
public class ExchangeList
{
    public ComponentGroup Group { get; set; }

    public List<ExchangeOption> Options { get; set; } = [];
}

public class ExchangeOption
{
    public string IngredientId { get; set; } = string.Empty;

    public double Qty { get; set; }

    public Unit Unit { get; set; }

    public FoodState State { get; set; } = FoodState.Crudo;
}

public class WeeklyBasic
{
    public string Meal { get; set; } = Meals.Colacion;

    public string IngredientId { get; set; } = string.Empty;

    public double QtyPerDay { get; set; }

    public Unit Unit { get; set; }

    public FoodState State { get; set; } = FoodState.Crudo;

    public int DaysPerWeek { get; set; } = 7;
}

public class PlanRules
{
    public List<string> ForbiddenMethods { get; set; } = [];

    /// <summary>Ingrediente que se sugiere como acompañamiento cuando una receta no trae verdura.</summary>
    public string? DefaultVegetableSideId { get; set; }

    public List<string> Notes { get; set; } = [];
}
