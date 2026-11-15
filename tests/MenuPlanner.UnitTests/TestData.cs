using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;
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

    public static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, AppJson.Options), AppJson.Options)!;
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Repositorios en memoria para probar los casos de uso sin MongoDB.</summary>
public sealed class InMemoryStore : IPlanRepository, IIngredientRepository, IRecipeRepository, IMenuRepository, ISettingsRepository
{
    public List<NutritionPlan> Plans { get; } = [];

    public List<Ingredient> IngredientList { get; } = [];

    public List<Recipe> RecipeList { get; } = [];

    public List<WeeklyMenu> Menus { get; } = [];

    public Pantry Pantry { get; set; } = new();

    public BasicsConfig Basics { get; set; } = new();

    public static InMemoryStore FromSeed()
    {
        var store = new InMemoryStore();
        var seed = TestData.Load();
        store.Plans.AddRange(seed.Plans.Select(TestData.Clone));
        store.IngredientList.AddRange(seed.Ingredients.Select(TestData.Clone));
        store.RecipeList.AddRange(seed.Recipes.Select(TestData.Clone));
        return store;
    }

    public MenuService CreateService(DateTimeOffset? now = null)
    {
        var time = new FixedTime(now ?? new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var converter = new UnitConverter();
        return new MenuService(this, this, this, this, this, new PortionCalculator(converter), new MenuGenerator(), new ShoppingListBuilder(converter, time), time);
    }

    Task<NutritionPlan?> IPlanRepository.GetActiveAsync(CancellationToken ct) =>
        Task.FromResult(Plans.Where(p => p.Active).OrderByDescending(p => p.Version).FirstOrDefault());

    Task<NutritionPlan?> IPlanRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Plans.FirstOrDefault(p => p.Id == id));

    Task<List<NutritionPlan>> IPlanRepository.ListAsync(CancellationToken ct) => Task.FromResult(Plans.ToList());

    Task IPlanRepository.UpsertAsync(NutritionPlan plan, CancellationToken ct)
    {
        Plans.RemoveAll(p => p.Id == plan.Id);
        Plans.Add(plan);
        return Task.CompletedTask;
    }

    Task IPlanRepository.ActivateAsync(string id, CancellationToken ct)
    {
        Plans.ForEach(p => p.Active = p.Id == id);
        return Task.CompletedTask;
    }

    Task<List<Ingredient>> IIngredientRepository.ListAsync(CancellationToken ct) => Task.FromResult(IngredientList.ToList());

    Task<Ingredient?> IIngredientRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(IngredientList.FirstOrDefault(i => i.Id == id));

    Task IIngredientRepository.UpsertAsync(Ingredient ingredient, CancellationToken ct)
    {
        IngredientList.RemoveAll(i => i.Id == ingredient.Id);
        IngredientList.Add(ingredient);
        return Task.CompletedTask;
    }

    Task IIngredientRepository.UpsertManyAsync(IEnumerable<Ingredient> ingredients, CancellationToken ct)
    {
        foreach (var ingredient in ingredients)
        {
            IngredientList.RemoveAll(i => i.Id == ingredient.Id);
            IngredientList.Add(ingredient);
        }

        return Task.CompletedTask;
    }

    Task<bool> IIngredientRepository.DeleteAsync(string id, CancellationToken ct) => Task.FromResult(IngredientList.RemoveAll(i => i.Id == id) > 0);

    Task<List<Recipe>> IRecipeRepository.ListAsync(string? meal, CancellationToken ct) =>
        Task.FromResult(RecipeList.Where(r => meal is null || r.Meal == meal).ToList());

    Task<Recipe?> IRecipeRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(RecipeList.FirstOrDefault(r => r.Id == id));

    Task IRecipeRepository.UpsertAsync(Recipe recipe, CancellationToken ct)
    {
        RecipeList.RemoveAll(r => r.Id == recipe.Id);
        RecipeList.Add(recipe);
        return Task.CompletedTask;
    }

    Task IRecipeRepository.UpsertManyAsync(IEnumerable<Recipe> recipes, CancellationToken ct)
    {
        foreach (var recipe in recipes)
        {
            RecipeList.RemoveAll(r => r.Id == recipe.Id);
            RecipeList.Add(recipe);
        }

        return Task.CompletedTask;
    }

    Task<bool> IRecipeRepository.DeleteAsync(string id, CancellationToken ct) => Task.FromResult(RecipeList.RemoveAll(r => r.Id == id) > 0);

    Task IRecipeRepository.RegisterCookedAsync(IEnumerable<string> recipeIds, DateTime date, CancellationToken ct)
    {
        foreach (var recipe in RecipeList.Where(r => recipeIds.Contains(r.Id)))
        {
            recipe.Stats.TimesCooked++;
            recipe.Stats.LastCooked = date;
        }

        return Task.CompletedTask;
    }

    Task<WeeklyMenu?> IMenuRepository.GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Menus.Where(m => m.Id == id).Select(TestData.Clone).FirstOrDefault());

    Task<List<WeeklyMenu>> IMenuRepository.ListAsync(int limit, CancellationToken ct) =>
        Task.FromResult(Menus.OrderByDescending(m => m.WeekOf).Take(limit).ToList());

    Task IMenuRepository.UpsertAsync(WeeklyMenu menu, CancellationToken ct)
    {
        Menus.RemoveAll(m => m.Id == menu.Id);
        Menus.Add(TestData.Clone(menu));
        return Task.CompletedTask;
    }

    Task<Pantry> ISettingsRepository.GetPantryAsync(CancellationToken ct) => Task.FromResult(Pantry);

    Task ISettingsRepository.SavePantryAsync(Pantry pantry, CancellationToken ct)
    {
        Pantry = pantry;
        return Task.CompletedTask;
    }

    Task<BasicsConfig> ISettingsRepository.GetBasicsAsync(CancellationToken ct) => Task.FromResult(Basics);

    Task ISettingsRepository.SaveBasicsAsync(BasicsConfig basics, CancellationToken ct)
    {
        Basics = basics;
        return Task.CompletedTask;
    }
}
