using MenuPlanner.Application.Menus;
using MenuPlanner.Domain;
using MenuPlanner.Infrastructure.Mongo;

namespace MenuPlanner.IntegrationTests;

[Collection(MongoCollection.Name)]
public class MongoTests(MongoFixture fixture)
{
    [SkippableFact]
    public async Task Seed_is_loaded_and_is_idempotent()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var db = fixture.Context!;

        var before = await new RecipeRepository(db).ListAsync();
        await Infrastructure.DependencyInjection.InitializeDatabaseAsync(
            new ProviderFor(db), Path.Combine(AppContext.BaseDirectory, "seed"), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var after = await new RecipeRepository(db).ListAsync();

        Assert.NotEmpty(before);
        Assert.Equal(before.Count, after.Count);
        Assert.NotNull(await new PlanRepository(db).GetActiveAsync());
    }

    [SkippableFact]
    public async Task Documents_round_trip_with_enums_and_nullables()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var repo = new IngredientRepository(fixture.Context!);

        var ingredient = new Ingredient
        {
            Id = "prueba-roundtrip",
            Name = "Prueba",
            Group = ComponentGroup.Leguminosa,
            BaseUnit = Unit.G,
            Conversions = [new UnitConversion { Unit = Unit.Taza, Grams = 170, State = FoodState.Cocido }],
            CookedYield = 2.5,
            Purchase = new PurchaseInfo { Unit = Unit.G, Step = 250 },
            Aisle = Aisles.Abarrotes,
        };
        await repo.UpsertAsync(ingredient);
        var loaded = await repo.GetAsync(ingredient.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ComponentGroup.Leguminosa, loaded!.Group);
        Assert.Equal(FoodState.Cocido, loaded.Conversions[0].State);
        Assert.Null(loaded.ProteinFamily);
        Assert.True(await repo.DeleteAsync(ingredient.Id));
    }

    [SkippableFact]
    public async Task Full_flow_generate_swap_confirm_and_plan_change()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var service = fixture.CreateMenuService();
        var plans = new PlanRepository(fixture.Context!);
        var recipes = new RecipeRepository(fixture.Context!);

        var menu = await service.GenerateAsync(new GenerateMenuRequest(5, new DateOnly(2026, 11, 16), Seed: 123));
        Assert.Equal(5, menu.Dishes.Count);

        var swapped = await service.SwapDishAsync(menu.Id, menu.Dishes[0].RecipeId, null);
        Assert.Equal(5, swapped.Dishes.Select(d => d.RecipeId).Distinct().Count());

        var confirmed = await service.ConfirmAsync(menu.Id);
        Assert.Equal(MenuStatus.Confirmed, confirmed.Status);

        var cooked = await recipes.GetAsync(confirmed.Dishes[0].RecipeId);
        Assert.True(cooked!.Stats.TimesCooked >= 1);
        Assert.Equal(new DateTime(2026, 11, 16), cooked.Stats.LastCooked!.Value.Date);

        var list = await service.GetShoppingListAsync(menu.Id);
        Assert.NotEmpty(list.Items);

        // Nueva versión del plan: el menú confirmado conserva su snapshot.
        var v2 = (await plans.GetActiveAsync())!;
        v2.Id = "plan-3";
        v2.Version = 3;
        v2.Meals.Single(m => m.Meal == Meals.Comida).GetSlot(ComponentGroup.Proteina)!.Max = 180;
        await plans.UpsertAsync(v2);
        await plans.ActivateAsync("plan-3");

        var old = await service.GetAsync(menu.Id);
        Assert.All(old.Dishes, d => Assert.Equal(200, d.Portioned.ProteinPerServingGrams));

        var fresh = await service.GenerateAsync(new GenerateMenuRequest(2, new DateOnly(2026, 11, 23), Seed: 1));
        Assert.All(fresh.Dishes, d => Assert.Equal(180, d.Portioned.ProteinPerServingGrams));

        await plans.ActivateAsync("plan-2");
    }

    [SkippableFact]
    public async Task Keep_import_round_trip_is_idempotent()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var service = fixture.CreateImportService();
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keep-sample-drive.md"));

        var job = await service.ImportKeepAsync(text, "Menus.md", latestYear: 2026, dryRun: false);
        Assert.Equal(5, job.NewNotes);
        Assert.Single(job.Unmatched);

        var again = await service.ImportKeepAsync(text, "Menus.md", latestYear: 2026, dryRun: false);
        Assert.Equal(0, again.NewNotes);

        var stored = await new ImportRepository(fixture.Context!).GetJobAsync(job.Id);
        Assert.Equal(job.DishOccurrences, stored!.DishOccurrences);
        Assert.Equal("platillo misterioso", stored.Unmatched[0].Name);

        var dish = await new DishRepository(fixture.Context!).GetAsync("enchiladas-suizas");
        Assert.True(dish!.Stats.TimesCooked >= 2);
        Assert.Equal(12, dish.Stats.Months.Count);

        var recipe = await new RecipeRepository(fixture.Context!).GetAsync("enchiladas-suizas");
        Assert.True(recipe!.Stats.HistoryCount >= 2);

        var reviewed = await service.ReviewAsync(job.Id, new MenuPlanner.Application.Imports.ReviewRequest(
            [new MenuPlanner.Application.Imports.ReviewMapping("platillo misterioso", Ignore: true)]));
        Assert.Empty(reviewed.Unmatched);

        var suggestions = await service.GetPantrySuggestionsAsync(12, 0.5);
        Assert.Contains(suggestions, s => s.IngredientId == "pierna-cerdo");
    }

    private sealed class ProviderFor(MongoContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(MongoContext) ? context : null;
    }
}
