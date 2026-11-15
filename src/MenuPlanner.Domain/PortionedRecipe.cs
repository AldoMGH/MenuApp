namespace MenuPlanner.Domain;

/// <summary>Receta con cantidades calculadas para la versión del plan. Se guarda como snapshot al confirmar un menú.</summary>
public class PortionedRecipe
{
    public string RecipeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Servings { get; set; }

    public string PlanId { get; set; } = string.Empty;

    public int PlanVersion { get; set; }

    public double ProteinPerServingGrams { get; set; }

    public bool Compatible { get; set; } = true;

    /// <summary>Lo que se compra y cocina para el lote completo (crudo, en unidad base).</summary>
    public List<PortionLine> Batch { get; set; } = [];

    /// <summary>Lo que te sirves en un plato (medidas del plan).</summary>
    public List<ServingLine> Serving { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<string> Suggestions { get; set; } = [];

    public List<string> Steps { get; set; } = [];
}

public class PortionLine
{
    public string IngredientId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ComponentGroup? Group { get; set; }

    public double Quantity { get; set; }

    public Unit Unit { get; set; }

    public string Display { get; set; } = string.Empty;

    /// <summary>true si la cantidad sale del plan; false si es un ingrediente fijo de la receta.</summary>
    public bool Scaled { get; set; }

    /// <summary>true si la app la agregó para completar el plato (la receta no la traía).</summary>
    public bool Suggested { get; set; }

    /// <summary>En cuántas porciones del lote va (leguminosa de acompañamiento puede ir en menos de 4).</summary>
    public int Servings { get; set; }
}

public class ServingLine
{
    public ComponentGroup Group { get; set; }

    public string Text { get; set; } = string.Empty;
}
