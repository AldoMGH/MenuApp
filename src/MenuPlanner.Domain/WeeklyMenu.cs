namespace MenuPlanner.Domain;

public class WeeklyMenu
{
    public string Id { get; set; } = string.Empty;

    public DateTime WeekOf { get; set; }

    public MenuStatus Status { get; set; } = MenuStatus.Draft;

    /// <summary>Semilla del muestreo: permite reproducir o regenerar la misma propuesta.</summary>
    public int Seed { get; set; }

    /// <summary>Contador de cambios de platillo, para que cada "cámbialo" dé una opción distinta pero reproducible.</summary>
    public int SwapCount { get; set; }

    public string PlanId { get; set; } = string.Empty;

    public int PlanVersion { get; set; }

    public List<MenuDish> Dishes { get; set; } = [];

    public List<string> GenerationWarnings { get; set; } = [];

    /// <summary>Snapshot de la lista al confirmar.</summary>
    public ShoppingList? ShoppingList { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }
}

public class MenuDish
{
    public string RecipeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Porciones del lote que llevan leguminosa como acompañamiento (0–4).</summary>
    public int LegumeSideServings { get; set; }

    /// <summary>Snapshot de la receta porcionada al confirmar; en borrador se calcula al vuelo.</summary>
    public PortionedRecipe? Snapshot { get; set; }
}

public class ShoppingList
{
    public DateTime GeneratedAt { get; set; }

    public List<ShoppingItem> Items { get; set; } = [];

    /// <summary>Lo que se necesita pero está marcado en la despensa.</summary>
    public List<ShoppingItem> AlreadyHave { get; set; } = [];
}

public class ShoppingItem
{
    public string IngredientId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Aisle { get; set; } = Aisles.Otros;

    public double NeededQuantity { get; set; }

    public Unit NeededUnit { get; set; }

    public string NeededDisplay { get; set; } = string.Empty;

    public double PurchaseQuantity { get; set; }

    public Unit PurchaseUnit { get; set; }

    public string PurchaseDisplay { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = [];
}

/// <summary>Lo que normalmente tienes en casa: se descuenta de la lista.</summary>
public class Pantry
{
    public string Id { get; set; } = "default";

    public List<string> IngredientIds { get; set; } = [];
}

/// <summary>Básicos semanales configurados por ti (desayunos, cenas habituales), adicionales a los del plan.</summary>
public class BasicsConfig
{
    public string Id { get; set; } = "default";

    public List<WeeklyBasic> Items { get; set; } = [];
}
