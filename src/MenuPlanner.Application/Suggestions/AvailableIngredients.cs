using MenuPlanner.Application.Imports;

namespace MenuPlanner.Application.Suggestions;

/// <summary>Lo que "sueles tener": despensa, básicos semanales y lo que compras en al menos 40% de las semanas.</summary>
public sealed record AvailableIngredient(string IngredientId, string Source, double Share);

public sealed class AvailableIngredients(ISettingsRepository settings, ImportService imports)
{
    public const double FrequentShare = 0.4;

    public async Task<Dictionary<string, AvailableIngredient>> LoadAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, AvailableIngredient>(StringComparer.Ordinal);
        foreach (var id in (await settings.GetPantryAsync(ct)).IngredientIds)
        {
            result[id] = new AvailableIngredient(id, "despensa", 1);
        }

        foreach (var basic in (await settings.GetBasicsAsync(ct)).Items)
        {
            result.TryAdd(basic.IngredientId, new AvailableIngredient(basic.IngredientId, "básicos", 1));
        }

        foreach (var suggestion in await imports.GetPantrySuggestionsAsync(6, FrequentShare, ct))
        {
            if (suggestion.IngredientId is { } id)
            {
                result.TryAdd(id, new AvailableIngredient(id, $"compras frecuentes ({suggestion.WeeksBought}/{suggestion.WeeksInWindow} semanas)", suggestion.Share));
            }
        }

        return result;
    }
}
