using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;
using MenuPlanner.Infrastructure.Memory;
using MenuPlanner.Portioning;

namespace MenuPlanner.UnitTests;

/// <summary>Carga el seed real del repo (copiado junto a las pruebas) para que las pruebas usen los mismos datos que la app.</summary>
public static class TestData
{
    private static readonly Lazy<SeedData> Seed = new(() => SeedData.Load(Path.Combine(AppContext.BaseDirectory, "seed")));

    public static SeedData Load() => Seed.Value;

    public static NutritionPlan Plan() => Clone(Seed.Value.Plans.Single(p => p.Active));

    public static Dictionary<string, Ingredient> Ingredients() => Seed.Value.Ingredients.Select(Clone).ToDictionary(i => i.Id);

    public static Recipe Recipe(string id) => Clone(Seed.Value.Recipes.Single(r => r.Id == id));

    public static List<Recipe> Recipes() => Seed.Value.Recipes.Select(Clone).ToList();

    public static SchemaValidator Schemas() => SchemaValidator.FromDirectory(Path.Combine(AppContext.BaseDirectory, "schemas"));

    public static PortionCalculator Calculator() => new(new UnitConverter());

    public static InMemoryStore Store() => InMemoryStore.FromSeed(Seed.Value);

    public static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, AppJson.Options), AppJson.Options)!;
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public static class InMemoryStoreTestExtensions
{
    public static MenuPlanner.Application.Imports.ImportService CreateImportService(this InMemoryStore store, DateTimeOffset? now = null) =>
        new(store, store, store, store, store, new FixedTime(now ?? new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero)));

    public static MenuService CreateService(this InMemoryStore store, DateTimeOffset? now = null)
    {
        var time = new FixedTime(now ?? new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var converter = new UnitConverter();
        return new MenuService(store, store, store, store, store, new PortionCalculator(converter), new MenuGenerator(), new ShoppingListBuilder(converter, time), time);
    }
}
