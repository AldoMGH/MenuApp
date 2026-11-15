using MenuPlanner.Domain;

namespace MenuPlanner.Application.Menus;

/// <summary>Receta candidata con los datos que necesita el generador.</summary>
public sealed record RecipeCandidate(Recipe Recipe, string? ProteinFamily, bool Compatible)
{
    public string Id => Recipe.Id;

    public LegumeMode LegumeMode => Recipe.LegumeMode;
}

public sealed class GenerationOptions
{
    /// <summary>Máximo de platillos con la misma proteína (pollo, res…) por menú.</summary>
    public int MaxSameProtein { get; init; } = 3;

    public int MaxLaborious { get; init; } = 1;

    /// <summary>A partir de cuántos platillos se exige al menos uno de pescado o mariscos.</summary>
    public int RequireFishFrom { get; init; } = 4;

    public IReadOnlyList<string> FishFamilies { get; init; } = ["pescado", "mariscos"];

    /// <summary>Si se cocinó hace menos de estos días, su peso se reduce.</summary>
    public int RecentDays { get; init; } = 21;

    public double RecentPenalty { get; init; } = 0.2;

    /// <summary>Multiplicador si en tu historial lo cocinaste en el mismo mes del año.</summary>
    public double SeasonBonus { get; init; } = 1.25;

    /// <summary>Máximo de platillos con leguminosa incluida (para no pasarse de las veces por semana).</summary>
    public int MaxLegumeIncludedDishes { get; init; } = int.MaxValue;

    /// <summary>Porciones mínimas que deben poder llevar leguminosa (incluida o de acompañamiento) para cumplir el plan.</summary>
    public int MinLegumeServings { get; init; }
}

public sealed record GenerationRequest(
    int DishCount,
    DateTime WeekOf,
    int Seed,
    IReadOnlyList<string> MustInclude,
    IReadOnlyList<string> Exclude,
    GenerationOptions Options);

public sealed record GenerationResult(IReadOnlyList<RecipeCandidate> Selected, IReadOnlyList<string> Warnings);

/// <summary>
/// Elige N platillos por muestreo ponderado (frecuencia histórica × penalización por recencia) respetando reglas
/// de variedad. Determinista para una misma semilla y catálogo.
/// </summary>
public sealed class MenuGenerator
{
    public GenerationResult Select(GenerationRequest request, IReadOnlyList<RecipeCandidate> candidates)
    {
        var warnings = new List<string>();
        var rng = new Random(request.Seed);
        var ordered = candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var exclude = new HashSet<string>(request.Exclude, StringComparer.OrdinalIgnoreCase);
        var selected = new List<RecipeCandidate>();

        foreach (var id in request.MustInclude)
        {
            var candidate = ordered.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                warnings.Add($"No se encontró la receta '{id}' que pediste incluir.");
                continue;
            }

            if (!candidate.Compatible)
            {
                warnings.Add($"'{candidate.Recipe.Name}' no cumple las reglas del plan, pero se incluyó porque lo pediste.");
            }

            if (!selected.Contains(candidate))
            {
                selected.Add(candidate);
            }
        }

        var pool = ordered
            .Where(c => c.Compatible && !exclude.Contains(c.Id) && !selected.Contains(c))
            .ToList();

        Fill(selected, pool, request, rng, enforceRules: true);
        if (selected.Count < request.DishCount && pool.Count > 0)
        {
            warnings.Add("No fue posible respetar todas las reglas de variedad con el catálogo actual; se relajaron para completar el menú.");
            Fill(selected, pool, request, rng, enforceRules: false);
        }

        if (selected.Count < request.DishCount)
        {
            warnings.Add($"Solo hay {selected.Count} platillos compatibles disponibles de los {request.DishCount} que pediste.");
        }

        EnsureFish(selected, pool, request, rng, warnings);
        EnsureLegume(selected, pool, request, rng);
        return new GenerationResult(selected, warnings);
    }

    /// <summary>Elige un reemplazo para un platillo del menú (para "cámbialo por otro").</summary>
    public RecipeCandidate? PickReplacement(
        IReadOnlyList<RecipeCandidate> currentWithoutReplaced,
        IReadOnlyList<RecipeCandidate> candidates,
        DateTime weekOf,
        int seed,
        GenerationOptions options)
    {
        var rng = new Random(seed);
        var request = new GenerationRequest(currentWithoutReplaced.Count + 1, weekOf, seed, [], [], options);
        var pool = candidates.Where(c => c.Compatible).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

        var withRules = pool.Where(c => SatisfiesRules(c, currentWithoutReplaced, options)).ToList();
        return Pick(withRules.Count > 0 ? withRules : pool, request, rng);
    }

    /// <summary>
    /// Peso = (1 + 2·√veces cocinado) × penalización si se cocinó hace poco × bonus de temporada.
    /// La raíz evita que tus 3–4 favoritos acaparen todos los menús.
    /// </summary>
    public static double Weight(RecipeCandidate candidate, DateTime weekOf, GenerationOptions options)
    {
        var stats = candidate.Recipe.Stats;
        var weight = 1.0 + (2 * Math.Sqrt(Math.Max(0, stats.TotalCount)));
        var lastCooked = stats.LatestCooked;
        if (lastCooked is not null && (weekOf - lastCooked.Value).TotalDays < options.RecentDays)
        {
            weight *= options.RecentPenalty;
        }

        if (stats.HistoryMonths.Count == 12 && stats.HistoryMonths[weekOf.Month - 1] > 0)
        {
            weight *= options.SeasonBonus;
        }

        return weight;
    }

    private static void Fill(List<RecipeCandidate> selected, List<RecipeCandidate> pool, GenerationRequest request, Random rng, bool enforceRules)
    {
        while (selected.Count < request.DishCount)
        {
            var eligible = enforceRules
                ? pool.Where(c => SatisfiesRules(c, selected, request.Options)).ToList()
                : pool;

            var pick = Pick(eligible, request, rng);
            if (pick is null)
            {
                return;
            }

            selected.Add(pick);
            pool.Remove(pick);
        }
    }

    private static void EnsureFish(List<RecipeCandidate> selected, List<RecipeCandidate> pool, GenerationRequest request, Random rng, List<string> warnings)
    {
        var options = request.Options;
        if (selected.Count < options.RequireFishFrom || selected.Any(c => IsFish(c, options)))
        {
            return;
        }

        var fish = pool.Where(c => IsFish(c, options)).ToList();
        var replaceable = selected.LastOrDefault(c => !request.MustInclude.Contains(c.Id, StringComparer.OrdinalIgnoreCase));
        if (fish.Count == 0 || replaceable is null)
        {
            warnings.Add("No hay platillos de pescado o mariscos disponibles para variar la semana.");
            return;
        }

        var others = selected.Where(c => c != replaceable).ToList();
        var candidates = fish.Where(c => SatisfiesRules(c, others, options)).ToList();
        var pick = Pick(candidates.Count > 0 ? candidates : fish, request, rng);
        if (pick is null)
        {
            return;
        }

        selected[selected.IndexOf(replaceable)] = pick;
        pool.Remove(pick);
        pool.Add(replaceable);
    }

    /// <summary>Si los platillos elegidos no alcanzan para la leguminosa semanal, cambia alguno por uno que la lleve.</summary>
    private static void EnsureLegume(List<RecipeCandidate> selected, List<RecipeCandidate> pool, GenerationRequest request, Random rng)
    {
        static int Capacity(IEnumerable<RecipeCandidate> dishes) =>
            dishes.Where(d => d.LegumeMode != LegumeMode.None).Sum(d => d.Recipe.Servings);

        var options = request.Options;
        while (Capacity(selected) < options.MinLegumeServings)
        {
            var fishCount = selected.Count(c => IsFish(c, options));
            var replaceable = selected.LastOrDefault(c =>
                c.LegumeMode == LegumeMode.None &&
                !request.MustInclude.Contains(c.Id, StringComparer.OrdinalIgnoreCase) &&
                !(IsFish(c, options) && fishCount == 1 && selected.Count >= options.RequireFishFrom));
            var legumes = pool.Where(c => c.LegumeMode != LegumeMode.None).ToList();
            if (replaceable is null || legumes.Count == 0)
            {
                return;
            }

            var others = selected.Where(c => c != replaceable).ToList();
            var eligible = legumes.Where(c => SatisfiesRules(c, others, options)).ToList();
            var pick = Pick(eligible.Count > 0 ? eligible : legumes, request, rng);
            if (pick is null)
            {
                return;
            }

            selected[selected.IndexOf(replaceable)] = pick;
            pool.Remove(pick);
            pool.Add(replaceable);
        }
    }

    private static bool SatisfiesRules(RecipeCandidate candidate, IReadOnlyList<RecipeCandidate> selected, GenerationOptions options)
    {
        if (candidate.ProteinFamily is not null &&
            selected.Count(s => string.Equals(s.ProteinFamily, candidate.ProteinFamily, StringComparison.OrdinalIgnoreCase)) >= options.MaxSameProtein)
        {
            return false;
        }

        if (candidate.Recipe.Laborious && selected.Count(s => s.Recipe.Laborious) >= options.MaxLaborious)
        {
            return false;
        }

        if (candidate.LegumeMode == LegumeMode.Included &&
            selected.Count(s => s.LegumeMode == LegumeMode.Included) >= options.MaxLegumeIncludedDishes)
        {
            return false;
        }

        return true;
    }

    private static bool IsFish(RecipeCandidate candidate, GenerationOptions options) =>
        candidate.ProteinFamily is not null && options.FishFamilies.Contains(candidate.ProteinFamily, StringComparer.OrdinalIgnoreCase);

    private static RecipeCandidate? Pick(IReadOnlyList<RecipeCandidate> eligible, GenerationRequest request, Random rng)
    {
        if (eligible.Count == 0)
        {
            return null;
        }

        var weights = eligible.Select(c => Weight(c, request.WeekOf, request.Options)).ToArray();
        var total = weights.Sum();
        var roll = rng.NextDouble() * total;
        for (var i = 0; i < eligible.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                return eligible[i];
            }
        }

        return eligible[^1];
    }
}
