namespace MenuPlanner.Domain;

public enum DishCompatibility
{
    /// <summary>Cumple el plan tal como lo cocinas.</summary>
    Compatible,

    /// <summary>Tiene una versión adaptada al plan (p. ej. al horno en vez de empanizado); conviene revisarla con la nutrióloga.</summary>
    Adaptada,

    /// <summary>No cumple el plan y no hay versión adaptada; el generador no lo propone.</summary>
    NoCompatible,
}

/// <summary>
/// Platillo de tu historial (lo que escribes en Keep: "Alambre", "Poyo ajonjolí"…). Puede ligarse a una
/// <see cref="Recipe"/> porcionada; sus estadísticas alimentan el peso del generador.
/// </summary>
public class Dish
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Formas en que aparece en tus notas (se comparan normalizadas: sin acentos ni mayúsculas).</summary>
    public List<string> Aliases { get; set; } = [];

    public string? RecipeId { get; set; }

    public DishCompatibility Compatibility { get; set; } = DishCompatibility.Compatible;

    public string? Notes { get; set; }

    public DishStats Stats { get; set; } = new();
}

public class DishStats
{
    public int TimesCooked { get; set; }

    public DateTime? FirstCooked { get; set; }

    public DateTime? LastCooked { get; set; }

    /// <summary>Veces por mes del año (índice 0 = enero) para el bonus de temporada.</summary>
    public List<int> Months { get; set; } = [.. new int[12]];
}

/// <summary>Reglas de normalización del importador (editables con la revisión).</summary>
public class ImportRules
{
    public string Id { get; set; } = "default";

    /// <summary>Correcciones por palabra: poyo → pollo, triyaki → teriyaki…</summary>
    public List<SpellingFix> SpellingFixes { get; set; } = [];

    /// <summary>Líneas de "Comidas" que no son platillos (normalizadas): "vinagre de arroz", "desayuno jueves"…</summary>
    public List<string> Ignore { get; set; } = [];
}

public class SpellingFix
{
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;
}
