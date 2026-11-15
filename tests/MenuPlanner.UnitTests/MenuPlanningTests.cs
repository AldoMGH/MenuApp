using MenuPlanner.Application.Menus;
using MenuPlanner.Domain;

namespace MenuPlanner.UnitTests;

public class LegumeBalancerTests
{
    private static readonly WeeklyFrequency TwoToThree = new() { Min = 2, Max = 3 };

    [Fact]
    public void Completes_the_weekly_target_with_sides()
    {
        LegumeDish[] dishes =
        [
            new("a", 4, LegumeMode.Included),
            new("b", 4, LegumeMode.Side),
            new("c", 4, LegumeMode.None),
            new("d", 4, LegumeMode.None),
            new("e", 4, LegumeMode.None),
        ];

        var balance = LegumeBalancer.Balance(dishes, TwoToThree, 7);

        // 20 porciones → entre 6 (20×2/7) y 8 (20×3/7); objetivo 7 = 4 incluidas + 3 de acompañamiento.
        Assert.Equal(6, balance.MinServings);
        Assert.Equal(8, balance.MaxServings);
        Assert.Equal(3, balance.SideServings["b"]);
        Assert.Equal(7, balance.LegumeServings);
        Assert.Equal(2.45, balance.MealsPerWeekEquivalent);
        Assert.Empty(balance.Warnings);
    }

    [Fact]
    public void Warns_when_there_is_no_legume_option()
    {
        var balance = LegumeBalancer.Balance([new("a", 4, LegumeMode.None), new("b", 4, LegumeMode.None)], TwoToThree, 7);

        Assert.Equal(0, balance.LegumeServings);
        Assert.Single(balance.Warnings);
    }

    [Fact]
    public void Warns_when_there_is_too_much_legume()
    {
        LegumeDish[] dishes = [new("a", 4, LegumeMode.Included), new("b", 4, LegumeMode.Included), new("c", 4, LegumeMode.Included)];

        var balance = LegumeBalancer.Balance(dishes, TwoToThree, 7);

        Assert.Equal(12, balance.LegumeServings);
        Assert.Contains(balance.Warnings, w => w.Contains("más de lo recomendado"));
    }
}

public class MenuGeneratorTests
{
    private static readonly DateTime WeekOf = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

    private static List<RecipeCandidate> Candidates()
    {
        var ingredients = TestData.Ingredients();
        return TestData.Recipes()
            .Select(r => new RecipeCandidate(
                r,
                ingredients[r.GetComponent(ComponentGroup.Proteina)!.Ingredients[0].IngredientId].ProteinFamily,
                Compatible: true))
            .ToList();
    }

    private static GenerationRequest Request(int seed, int count = 5, string[]? must = null, string[]? exclude = null) =>
        new(count, WeekOf, seed, must ?? [], exclude ?? [], new GenerationOptions { MaxSameProtein = 2, MaxLegumeIncludedDishes = 2 });

    [Fact]
    public void Same_seed_gives_the_same_menu()
    {
        var a = new MenuGenerator().Select(Request(42), Candidates());
        var b = new MenuGenerator().Select(Request(42), Candidates());

        Assert.Equal(a.Selected.Select(c => c.Id), b.Selected.Select(c => c.Id));
    }

    [Fact]
    public void Respects_variety_rules_across_many_seeds()
    {
        var generator = new MenuGenerator();
        for (var seed = 1; seed <= 200; seed++)
        {
            var result = generator.Select(Request(seed), Candidates());

            Assert.Equal(5, result.Selected.Count);
            Assert.Equal(5, result.Selected.Select(c => c.Id).Distinct().Count());
            Assert.All(result.Selected.GroupBy(c => c.ProteinFamily), g => Assert.True(g.Count() <= 2, $"seed {seed}: {g.Key} × {g.Count()}"));
            Assert.True(result.Selected.Count(c => c.Recipe.Laborious) <= 1, $"seed {seed}: más de un platillo laborioso");
            Assert.True(result.Selected.Count(c => c.LegumeMode == LegumeMode.Included) <= 2, $"seed {seed}: demasiada leguminosa");
            Assert.Contains(result.Selected, c => c.ProteinFamily is "pescado" or "mariscos");
        }
    }

    [Fact]
    public void Honors_must_include_and_exclude()
    {
        var result = new MenuGenerator().Select(Request(7, must: ["tinga-pollo"], exclude: ["alambre", "pollo-teriyaki"]), Candidates());

        Assert.Equal("tinga-pollo", result.Selected[0].Id);
        Assert.DoesNotContain(result.Selected, c => c.Id is "alambre" or "pollo-teriyaki");
    }

    [Fact]
    public void Recently_cooked_recipes_are_less_likely()
    {
        var candidates = Candidates();
        var recent = candidates.First();
        recent.Recipe.Stats.LastCooked = WeekOf.AddDays(-7);

        var options = new GenerationOptions();
        Assert.Equal(0.2, MenuGenerator.Weight(recent, WeekOf, options), 3);
        Assert.Equal(1, MenuGenerator.Weight(candidates[1], WeekOf, options), 3);
    }

    [Fact]
    public void Warns_when_the_catalog_is_too_small()
    {
        var total = Candidates().Count;
        var result = new MenuGenerator().Select(Request(1, count: total + 5), Candidates());

        Assert.Equal(total, result.Selected.Count);
        Assert.Contains(result.Warnings, w => w.Contains($"Solo hay {total}"));
    }
}
