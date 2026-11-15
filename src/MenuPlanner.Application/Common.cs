using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using MenuPlanner.Domain;

namespace MenuPlanner.Application;

public class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class BadRequestException(string message, IReadOnlyList<string>? errors = null) : AppException(message)
{
    public IReadOnlyList<string> Errors { get; } = errors ?? [];
}

/// <summary>Opciones JSON compartidas por la API, el seed y las pruebas (camelCase, enums como texto).</summary>
public static class AppJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.ReadCommentHandling = JsonCommentHandling.Skip;
        options.AllowTrailingCommas = true;
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        }

        return options;
    }
}

public interface IPlanRepository
{
    Task<NutritionPlan?> GetActiveAsync(CancellationToken ct = default);

    Task<NutritionPlan?> GetAsync(string id, CancellationToken ct = default);

    Task<List<NutritionPlan>> ListAsync(CancellationToken ct = default);

    Task UpsertAsync(NutritionPlan plan, CancellationToken ct = default);

    /// <summary>Marca el plan indicado como activo y desactiva los demás.</summary>
    Task ActivateAsync(string id, CancellationToken ct = default);
}

public interface IIngredientRepository
{
    Task<List<Ingredient>> ListAsync(CancellationToken ct = default);

    Task<Ingredient?> GetAsync(string id, CancellationToken ct = default);

    Task UpsertAsync(Ingredient ingredient, CancellationToken ct = default);

    Task UpsertManyAsync(IEnumerable<Ingredient> ingredients, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public interface IRecipeRepository
{
    Task<List<Recipe>> ListAsync(string? meal = null, CancellationToken ct = default);

    Task<Recipe?> GetAsync(string id, CancellationToken ct = default);

    Task UpsertAsync(Recipe recipe, CancellationToken ct = default);

    Task UpsertManyAsync(IEnumerable<Recipe> recipes, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Incrementa <c>timesCooked</c> y fija <c>lastCooked</c> para las recetas de un menú confirmado.</summary>
    Task RegisterCookedAsync(IEnumerable<string> recipeIds, DateTime date, CancellationToken ct = default);
}

public interface IMenuRepository
{
    Task<WeeklyMenu?> GetAsync(string id, CancellationToken ct = default);

    Task<List<WeeklyMenu>> ListAsync(int limit = 20, CancellationToken ct = default);

    Task UpsertAsync(WeeklyMenu menu, CancellationToken ct = default);
}

public interface ISettingsRepository
{
    Task<Pantry> GetPantryAsync(CancellationToken ct = default);

    Task SavePantryAsync(Pantry pantry, CancellationToken ct = default);

    Task<BasicsConfig> GetBasicsAsync(CancellationToken ct = default);

    Task SaveBasicsAsync(BasicsConfig basics, CancellationToken ct = default);
}
