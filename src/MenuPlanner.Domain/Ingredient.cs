namespace MenuPlanner.Domain;

/// <summary>
/// Ingrediente del catálogo maestro. Las cantidades se expresan en <see cref="BaseUnit"/> y en estado crudo
/// (lo que se compra); <see cref="Conversions"/> y <see cref="CookedYield"/> permiten traducir medidas caseras
/// y cantidades cocidas del plan a esa unidad base.
/// </summary>
public class Ingredient
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public ComponentGroup? Group { get; set; }

    /// <summary>Unidad canónica (cruda) en la que se suman cantidades: g, ml, pieza, diente o paquete.</summary>
    public Unit BaseUnit { get; set; } = Unit.G;

    /// <summary>Equivalencias de 1 unidad (taza, pieza, cda…) a gramos, en el estado indicado.</summary>
    public List<UnitConversion> Conversions { get; set; } = [];

    /// <summary>Gramos cocidos que rinde 1 g crudo (pollo ≈ 0.72, arroz ≈ 2.9). 1 = no cambia.</summary>
    public double CookedYield { get; set; } = 1.0;

    public PurchaseInfo Purchase { get; set; } = new();

    public string Aisle { get; set; } = Aisles.Otros;

    /// <summary>Para proteínas: si es magra según el plan.</summary>
    public bool IsLean { get; set; } = true;

    /// <summary>Familia de proteína para reglas de variedad (pollo, res, cerdo, pescado, mariscos).</summary>
    public string? ProteinFamily { get; set; }

    public string? Notes { get; set; }

    /// <summary>Nutrición aproximada por 100 g (o 100 ml) de la unidad base en crudo. Solo informativa.</summary>
    public NutritionFacts? Nutrition { get; set; }
}

/// <summary>Energía y macronutrimentos. Por 100 g en el catálogo; por porción o por lote en los resultados.</summary>
public class NutritionFacts
{
    public double Kcal { get; set; }

    public double Protein { get; set; }

    public double Carbs { get; set; }

    public double Fat { get; set; }

    public double Fiber { get; set; }
}

/// <summary>1 <see cref="Unit"/> del ingrediente en estado <see cref="State"/> pesa <see cref="Grams"/> gramos (en ese mismo estado).</summary>
public class UnitConversion
{
    public Unit Unit { get; set; }

    public double Grams { get; set; }

    public FoodState State { get; set; } = FoodState.Crudo;
}

/// <summary>Cómo se compra el ingrediente: unidad, incremento mínimo y etiqueta opcional ("manojo", "cabeza").</summary>
public class PurchaseInfo
{
    public Unit Unit { get; set; } = Unit.G;

    /// <summary>Incremento de compra en <see cref="Unit"/> (50 g en carnicería, 1 pieza, 250 g de tortilla…).</summary>
    public double Step { get; set; } = 1;

    public string? Label { get; set; }
}

public static class Aisles
{
    public const string FrutasVerduras = "Frutas y verduras";
    public const string Carnes = "Carnes y pescados";
    public const string Lacteos = "Lácteos y huevo";
    public const string Tortilleria = "Tortillería y panadería";
    public const string Abarrotes = "Abarrotes";
    public const string Semillas = "Semillas y especias";
    public const string Otros = "Otros";

    public static readonly IReadOnlyList<string> Order =
        [FrutasVerduras, Carnes, Lacteos, Tortilleria, Abarrotes, Semillas, Otros];
}
