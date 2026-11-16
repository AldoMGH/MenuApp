namespace MenuPlanner.Domain;

public enum MatchStatus
{
    Matched,
    Fuzzy,
    Unmatched,
    Ignored,
}

/// <summary>Sección de la nota de Keep de la que viene un renglón.</summary>
public enum NoteSection
{
    Comidas,
    Carnes,
    Verduras,
    Procesados,
}

/// <summary>Nota ya importada; su id es el hash del contenido y garantiza que reimportar no duplique.</summary>
public class ImportedNote
{
    public string Id { get; set; } = string.Empty;

    public string ImportId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public int Position { get; set; }
}

/// <summary>Un platillo escrito en una nota ("dishHistory").</summary>
public class DishOccurrence
{
    public string Id { get; set; } = string.Empty;

    public string NoteId { get; set; } = string.Empty;

    public string ImportId { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public string NoteTitle { get; set; } = string.Empty;

    public string RawName { get; set; } = string.Empty;

    public string Normalized { get; set; } = string.Empty;

    public string? DishId { get; set; }

    /// <summary>Lo que venía después de "/" o "+": acompañamiento o alternativa ("lentejas", "ensalada con betabel").</summary>
    public string? Side { get; set; }

    public MatchStatus Status { get; set; }

    public double Confidence { get; set; }
}

/// <summary>Renglón de las listas de compras (Carnes, Verduras, Procesados).</summary>
public class PurchaseRecord
{
    public string Id { get; set; } = string.Empty;

    public string NoteId { get; set; } = string.Empty;

    public string ImportId { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public NoteSection Section { get; set; }

    public string Raw { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double? Qty { get; set; }

    public Unit? Unit { get; set; }

    public string? IngredientId { get; set; }
}

public class ImportJob
{
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? FileName { get; set; }

    public bool DryRun { get; set; }

    public int LatestYear { get; set; }

    public int TotalNotes { get; set; }

    public int NewNotes { get; set; }

    public int DuplicateNotes { get; set; }

    public DateTime? FirstDate { get; set; }

    public DateTime? LastDate { get; set; }

    public int DishOccurrences { get; set; }

    public int Matched { get; set; }

    public int Fuzzy { get; set; }

    public int Ignored { get; set; }

    public int PurchaseRecords { get; set; }

    public int PurchasesMatched { get; set; }

    /// <summary>Nombres sin asignar (normalizados) con su frecuencia y sugerencias, para la revisión.</summary>
    public List<UnmatchedName> Unmatched { get; set; } = [];

    /// <summary>Asignaciones aproximadas, para que confirmes que son correctas.</summary>
    public List<FuzzyMatch> FuzzyMatches { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public double MatchRate => DishOccurrences - Ignored == 0 ? 1 : Math.Round((Matched + Fuzzy) / (double)(DishOccurrences - Ignored), 3);
}

public class UnmatchedName
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<string> Examples { get; set; } = [];

    public List<DishSuggestion> Suggestions { get; set; } = [];
}

public class FuzzyMatch
{
    public string Name { get; set; } = string.Empty;

    public string DishId { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public int Count { get; set; }
}

public class DishSuggestion
{
    public string DishId { get; set; } = string.Empty;

    public string DishName { get; set; } = string.Empty;

    public double Score { get; set; }
}
