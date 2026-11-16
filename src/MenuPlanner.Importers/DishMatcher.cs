using MenuPlanner.Domain;

namespace MenuPlanner.Importers;

public sealed record DishMatch(
    string Normalized,
    string? DishId,
    string? Side,
    MatchStatus Status,
    double Confidence,
    IReadOnlyList<DishSuggestion> Suggestions);

/// <summary>
/// Asigna cada renglón de "Comidas" a un platillo del catálogo:
/// 1) correcciones de ortografía por palabra, 2) separa acompañamiento ("/" o "+"),
/// 3) alias exacto, 4) alias contenido en el texto ("teriyaki con arroz" → teriyaki), 5) similitud aproximada.
/// </summary>
public sealed class DishMatcher
{
    public const double FuzzyThreshold = 0.85;

    private readonly Dictionary<string, string> _fixes;
    private readonly HashSet<string> _ignore;
    private readonly Dictionary<string, (string DishId, string Name)> _aliases = new(StringComparer.Ordinal);
    private readonly List<(string Alias, string[] Tokens, string DishId, string Name)> _aliasTokens = [];

    public DishMatcher(IEnumerable<Dish> dishes, ImportRules rules)
    {
        _fixes = rules.SpellingFixes
            .GroupBy(f => TextNormalizer.Key(f.From))
            .ToDictionary(g => g.Key, g => TextNormalizer.Key(g.First().To), StringComparer.Ordinal);
        _ignore = rules.Ignore.Select(Normalize).ToHashSet(StringComparer.Ordinal);

        foreach (var dish in dishes)
        {
            foreach (var alias in dish.Aliases.Append(dish.Name).Append(dish.Id.Replace('-', ' ')))
            {
                var key = Normalize(alias);
                if (key.Length == 0 || _aliases.ContainsKey(key))
                {
                    continue;
                }

                _aliases[key] = (dish.Id, dish.Name);
                var tokens = TextNormalizer.MeaningfulTokens(key);
                if (tokens.Length > 0)
                {
                    _aliasTokens.Add((key, tokens, dish.Id, dish.Name));
                }
            }
        }
    }

    /// <summary>Clave normalizada con correcciones de ortografía aplicadas (lo que se guarda como alias en la revisión).</summary>
    public string Normalize(string raw)
    {
        var key = TextNormalizer.Key(raw);
        if (key.Length == 0)
        {
            return key;
        }

        var joined = TextNormalizer.ReplaceWords(key, _fixes);
        foreach (var (from, to) in _fixes.Where(f => f.Key.Contains(' ', StringComparison.Ordinal)))
        {
            joined = joined.Replace(from, to, StringComparison.Ordinal);
        }

        return joined;
    }

    public DishMatch Match(string raw)
    {
        var normalized = Normalize(raw);
        if (normalized.Length == 0 || _ignore.Contains(normalized))
        {
            return new DishMatch(normalized, null, null, MatchStatus.Ignored, 1, []);
        }

        if (_aliases.TryGetValue(normalized, out var exact))
        {
            return new DishMatch(normalized, exact.DishId, null, MatchStatus.Matched, 1, []);
        }

        var parts = normalized.Split(new[] { '/', '+' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var main = parts.FirstOrDefault(p => !_ignore.Contains(p)) ?? string.Empty;
        var side = parts.Length > 1 ? string.Join(" / ", parts.Where(p => p != main)) : null;
        if (main.Length == 0)
        {
            return new DishMatch(normalized, null, side, MatchStatus.Ignored, 1, []);
        }

        if (_aliases.TryGetValue(main, out var mainExact))
        {
            return new DishMatch(normalized, mainExact.DishId, side, MatchStatus.Matched, 1, []);
        }

        var contained = Contained(main);
        if (contained is not null)
        {
            return new DishMatch(normalized, contained.Value.DishId, side, MatchStatus.Matched, 0.95, []);
        }

        var ranked = Rank(main);
        var best = ranked.FirstOrDefault();
        if (best is not null && best.Score >= FuzzyThreshold)
        {
            return new DishMatch(normalized, best.DishId, side, MatchStatus.Fuzzy, best.Score, ranked);
        }

        return new DishMatch(normalized, null, side, MatchStatus.Unmatched, best?.Score ?? 0, ranked);
    }

    /// <summary>Alias cuyas palabras significativas están todas en el texto; gana el más largo.</summary>
    private (string DishId, string Name)? Contained(string text)
    {
        var tokens = TextNormalizer.Tokens(text).ToHashSet(StringComparer.Ordinal);
        var best = _aliasTokens
            .Where(a => a.Tokens.All(tokens.Contains))
            .OrderByDescending(a => a.Tokens.Length)
            .ThenByDescending(a => a.Alias.Length)
            .FirstOrDefault();
        return best.DishId is null ? null : (best.DishId, best.Name);
    }

    private List<DishSuggestion> Rank(string text) =>
        _aliases
            .Select(a => new DishSuggestion { DishId = a.Value.DishId, DishName = a.Value.Name, Score = Math.Round(TextNormalizer.Similarity(text, a.Key), 3) })
            .GroupBy(s => s.DishId)
            .Select(g => g.OrderByDescending(s => s.Score).First())
            .OrderByDescending(s => s.Score)
            .Take(3)
            .ToList();
}

/// <summary>Liga renglones de compras ("1 kg piernil deshuesado") con ingredientes del catálogo por nombre o alias.</summary>
public sealed class IngredientMatcher
{
    private readonly List<(string[] Tokens, string Key, string IngredientId)> _aliases = [];

    public IngredientMatcher(IEnumerable<Ingredient> ingredients, ImportRules rules)
    {
        var fixes = rules.SpellingFixes.ToDictionary(f => TextNormalizer.Key(f.From), f => TextNormalizer.Key(f.To), StringComparer.Ordinal);
        Fixes = fixes;
        foreach (var ingredient in ingredients)
        {
            foreach (var name in ingredient.Aliases.Append(ingredient.Name))
            {
                var key = TextNormalizer.Key(name);
                var tokens = TextNormalizer.MeaningfulTokens(key);
                if (tokens.Length > 0)
                {
                    _aliases.Add((tokens, key, ingredient.Id));
                }
            }
        }
    }

    private Dictionary<string, string> Fixes { get; }

    public string Normalize(string name) => TextNormalizer.ReplaceWords(TextNormalizer.Key(name), Fixes);

    public string? Match(string name)
    {
        var tokens = TextNormalizer.Tokens(Normalize(name)).ToHashSet(StringComparer.Ordinal);
        if (tokens.Count == 0)
        {
            return null;
        }

        // Plurales simples: "pepinos" → "pepino", "tomates" → "tomate".
        foreach (var token in tokens.ToList())
        {
            if (token.Length > 3 && token.EndsWith('s'))
            {
                tokens.Add(token[..^1]);
                if (token.EndsWith("es", StringComparison.Ordinal))
                {
                    tokens.Add(token[..^2]);
                }
            }
        }

        var best = _aliases
            .Where(a => a.Tokens.All(tokens.Contains))
            .OrderByDescending(a => a.Tokens.Length)
            .ThenByDescending(a => a.Key.Length)
            .FirstOrDefault();
        return best.IngredientId;
    }
}
