using MenuPlanner.Application.Suggestions;
using MenuPlanner.Domain;
using MenuPlanner.Infrastructure.Memory;
using MenuPlanner.Portioning;

namespace MenuPlanner.UnitTests;

public class NutritionTests
{
    [Fact]
    public void Every_seed_ingredient_has_nutrition()
    {
        Assert.All(TestData.Load().Ingredients, i => Assert.True(i.Nutrition is not null, i.Id));
    }

    [Fact]
    public void Teriyaki_macros_match_a_hand_calculation()
    {
        // Lote: pechuga 1120 g, brócoli 360 g, zanahoria 260 g, pimiento 300 g, arroz 135 g, soya 60 ml,
        // miel 1 cda (15), jengibre 10 g, ajo 2 dientes (10 g), ajonjolí 1 cda (9 g) → 2308 kcal y 283.6 g de proteína.
        var result = TestData.Calculator().Calculate(TestData.Recipe("pollo-teriyaki"), TestData.Plan(), TestData.Ingredients());

        var n = Assert.IsType<RecipeNutrition>(result.Nutrition);
        Assert.Empty(n.Missing);
        Assert.Equal(2308, n.Batch.Kcal, 0);
        Assert.Equal(283.6, n.Batch.Protein, 0);
        Assert.Equal(577, n.PerServing.Kcal, 0);
        Assert.Equal(70.9, n.PerServing.Protein, 0);
    }

    [Fact]
    public void Missing_data_is_reported_not_guessed()
    {
        var ingredients = TestData.Ingredients();
        ingredients["miel"].Nutrition = null;

        var n = TestData.Calculator().Calculate(TestData.Recipe("pollo-teriyaki"), TestData.Plan(), ingredients).Nutrition!;

        Assert.Contains("Miel", n.Missing);
        Assert.Equal(2308 - 65, n.Batch.Kcal, 0);
    }

    [Fact]
    public void Every_seed_recipe_has_complete_macros()
    {
        var calculator = TestData.Calculator();
        foreach (var recipe in TestData.Recipes())
        {
            var n = calculator.Calculate(recipe, TestData.Plan(), TestData.Ingredients()).Nutrition!;
            Assert.True(n.Missing.Count == 0, $"{recipe.Id}: {string.Join(", ", n.Missing)}");
            Assert.InRange(n.PerServing.Kcal, 250, 900);
            Assert.InRange(n.PerServing.Protein, 30, 80);
        }
    }
}

public class SuggestionServicesTests
{
    private static (InMemoryStore Store, AvailableIngredients Available, RecipeDraftService Drafts) Build()
    {
        var store = TestData.Store();
        var available = new AvailableIngredients(store, store.CreateImportService());
        var drafts = new RecipeDraftService(TestData.Schemas(), store, store, store, store, available, TestData.Calculator(), store.CreateService());
        return (store, available, drafts);
    }

    private const string ValidDraft = """
        {
          "name": "Pescado al ajillo con ejotes y quinoa",
          "cookingMethod": "plancha",
          "tags": ["pescado"],
          "components": [
            { "group": "proteina", "ingredients": [ { "ingredientId": "filete-pescado" } ] },
            { "group": "verdura", "ingredients": [ { "ingredientId": "ejote", "share": 0.5 }, { "ingredientId": "jitomate", "share": 0.5 } ] },
            { "group": "cereal", "ingredients": [ { "ingredientId": "quinoa" } ] }
          ],
          "fixedIngredients": [ { "ingredientId": "ajo", "qty": 4, "unit": "diente" }, { "ingredientId": "limon", "qty": 2, "unit": "pieza" } ],
          "steps": ["Dora el ajo, cocina el pescado y sirve con ejotes y quinoa."]
        }
        """;

    [Fact]
    public async Task Cookability_ranks_by_what_you_have()
    {
        var (store, available, _) = Build();
        store.Pantry = new Pantry { IngredientIds = ["pechuga-pollo", "arroz", "brocoli", "zanahoria", "pimiento", "salsa-soya", "ajo", "jengibre", "miel", "ajonjoli"] };
        var cookability = new PantryCookability(store, store, store, available, TestData.Calculator());

        var ranked = await cookability.RankAsync(5);

        Assert.Equal("pollo-teriyaki", ranked[0].RecipeId);
        Assert.Equal(1.0, ranked[0].Coverage);
        Assert.Empty(ranked[0].Missing);
        Assert.Equal(ranked.OrderByDescending(r => r.Coverage).Select(r => r.RecipeId), ranked.Select(r => r.RecipeId));
        Assert.All(ranked.Skip(1), r => Assert.NotEmpty(r.Missing));
    }

    [Fact]
    public async Task Preview_validates_portions_and_does_not_save()
    {
        var (store, _, drafts) = Build();
        var before = store.RecipeList.Count;

        var draft = await drafts.PreviewAsync(ValidDraft);

        Assert.True(draft.Valid, string.Join("; ", draft.Errors));
        Assert.Equal("pescado-al-ajillo-con-ejotes-y-quinoa", draft.Recipe!.Id);
        Assert.Equal(1000, draft.Portioned!.Batch.Single(l => l.IngredientId == "filete-pescado").Quantity);
        Assert.NotNull(draft.Portioned.Nutrition);
        Assert.NotNull(draft.Availability);
        Assert.Equal(before, store.RecipeList.Count);
    }

    [Theory]
    [InlineData("{ no es json", "no es válido")]
    [InlineData("""{ "name": "Algo", "cookingMethod": "guisado", "components": [ { "group": "proteina", "ingredients": [ { "ingredientId": "salmon-ahumado" } ] } ] }""", "¿Quisiste decir")]
    [InlineData("""{ "name": "Milanesa", "cookingMethod": "empanizado", "components": [ { "group": "proteina", "ingredients": [ { "ingredientId": "bistec-res-magro" } ] } ] }""", "empanizado")]
    [InlineData("""{ "name": "Pollo con pan", "cookingMethod": "plancha", "components": [ { "group": "proteina", "ingredients": [ { "ingredientId": "pechuga-pollo" } ] }, { "group": "cereal", "ingredients": [ { "ingredientId": "tortilla-harina-integral" } ] } ] }""", "intercambios")]
    [InlineData("""{ "name": "Sin componentes", "cookingMethod": "plancha" }""", "components")]
    public async Task Preview_explains_what_to_fix(string json, string expected)
    {
        var (_, _, drafts) = Build();

        var draft = await drafts.PreviewAsync(json);

        Assert.False(draft.Valid);
        Assert.Contains(draft.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Save_marks_for_review_creates_the_dish_and_adds_to_menu()
    {
        var (store, _, drafts) = Build();
        await store.CreateService().GenerateAsync(new GenerateMenuRequest(3, Seed: 2));

        var (_, saved) = await drafts.SaveAsync(ValidDraft, addToMenu: true, menuId: null);
        var (_, again) = await drafts.SaveAsync(ValidDraft, addToMenu: false, menuId: null);

        Assert.NotNull(saved);
        var recipe = store.RecipeList.Single(r => r.Id == saved!.Recipe.Id);
        Assert.Equal(RecipeDraftService.Source, recipe.Source);
        Assert.Contains(RecipeDraftService.ReviewTag, recipe.Tags);
        Assert.Equal(recipe.Id, store.DishList.Single(d => d.Id == saved!.Dish.Id).RecipeId);
        Assert.Contains(store.Menus.Single().Dishes, d => d.RecipeId == recipe.Id);
        Assert.Equal($"{recipe.Id}-2", again!.Recipe.Id);                         // no pisa la anterior
    }
}
