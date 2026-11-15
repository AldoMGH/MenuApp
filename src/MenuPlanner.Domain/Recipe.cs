using System.Text.Json.Serialization;

namespace MenuPlanner.Domain;

/// <summary>
/// Receta = guisado + acompañamientos. Los <see cref="Components"/> se escalan para cumplir el plan;
/// los <see cref="FixedIngredients"/> (salsa, condimentos) son la identidad del platillo y no se escalan.
/// </summary>
public class Recipe
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Meal { get; set; } = Meals.Comida;

    public int Servings { get; set; } = 4;

    public string CookingMethod { get; set; } = "guisado";

    public List<string> Tags { get; set; } = [];

    /// <summary>Platillo que toma más tiempo; el generador limita cuántos entran por semana.</summary>
    public bool Laborious { get; set; }

    public List<RecipeComponent> Components { get; set; } = [];

    public List<RecipeIngredient> FixedIngredients { get; set; } = [];

    public List<string> Steps { get; set; } = [];

    public string Source { get; set; } = "manual";

    public RecipeStats Stats { get; set; } = new();

    public RecipeComponent? GetComponent(ComponentGroup group) => Components.FirstOrDefault(c => c.Group == group);

    [JsonIgnore]
    public LegumeMode LegumeMode
    {
        get
        {
            var legume = GetComponent(ComponentGroup.Leguminosa);
            if (legume is null || legume.Ingredients.Count == 0)
            {
                return LegumeMode.None;
            }

            return legume.Optional ? LegumeMode.Side : LegumeMode.Included;
        }
    }
}

public class RecipeComponent
{
    public ComponentGroup Group { get; set; }

    /// <summary>Ingredientes que cubren el componente; la cantidad del plan se reparte según <see cref="ComponentIngredient.Share"/>.</summary>
    public List<ComponentIngredient> Ingredients { get; set; } = [];

    /// <summary>Solo aplica a leguminosa: acompañamiento opcional que el menú activa en algunas porciones.</summary>
    public bool Optional { get; set; }
}

public class ComponentIngredient
{
    public string IngredientId { get; set; } = string.Empty;

    public double Share { get; set; } = 1;
}

public class RecipeIngredient
{
    public string IngredientId { get; set; } = string.Empty;

    public double Qty { get; set; }

    public Unit Unit { get; set; }

    public string? Note { get; set; }
}

public class RecipeStats
{
    public int TimesCooked { get; set; }

    public DateTime? LastCooked { get; set; }
}
