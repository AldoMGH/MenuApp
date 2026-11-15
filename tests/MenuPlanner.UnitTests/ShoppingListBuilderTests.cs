using MenuPlanner.Application.Shopping;
using MenuPlanner.Domain;
using MenuPlanner.Portioning;

namespace MenuPlanner.UnitTests;

public class ShoppingListBuilderTests
{
    private readonly Dictionary<string, Ingredient> _ingredients = TestData.Ingredients();
    private readonly NutritionPlan _plan = TestData.Plan();

    [Fact]
    public void Builds_a_golden_list_with_basics_pantry_and_purchase_rounding()
    {
        var calculator = TestData.Calculator();
        var lomo = calculator.Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients);
        var lentejas = calculator.Calculate(TestData.Recipe("lentejas-tacos-pescado"), _plan, _ingredients);
        var builder = new ShoppingListBuilder(new UnitConverter(), new FixedTime(DateTimeOffset.UnixEpoch));

        var list = builder.Build([lomo, lentejas], _plan.WeeklyBasics, _ingredients, pantry: ["chia"]);

        // Tortillas: 8 + 8 piezas = 480 g → se compran de 250 en 250 g.
        AssertItem(list, "tortilla-maiz", "500 g", "Tortillería y panadería");
        // Carnicería en incrementos de 50 g.
        AssertItem(list, "lomo-cerdo", "1.15 kg", "Carnes y pescados");
        AssertItem(list, "filete-pescado", "1 kg", "Carnes y pescados");
        // Colación × 7: 7 tz de fruta (1.05 kg → 1.5 kg) y 3½ tz de yogurt (840 ml → 1 L).
        AssertItem(list, "fruta-temporada", "1.5 kg", "Frutas y verduras");
        AssertItem(list, "yogurt-natural", "1 L", "Lácteos y huevo");
        // Ajo: 2 dientes → 1 cabeza. Cilantro: ½ + ½ manojo → 1 manojo.
        AssertItem(list, "ajo", "1 cabeza", "Frutas y verduras");
        AssertItem(list, "cilantro", "1 manojo", "Frutas y verduras");
        // Limón: 4 piezas (160 g) → 250 g.
        AssertItem(list, "limon", "250 g", "Frutas y verduras");

        Assert.Contains(list.AlreadyHave, i => i.IngredientId == "chia");
        Assert.DoesNotContain(list.Items, i => i.IngredientId == "chia");

        var cilantro = list.Items.Single(i => i.IngredientId == "cilantro");
        Assert.Equal(2, cilantro.Sources.Count);

        // Ordenado por pasillo.
        var aisles = list.Items.Select(i => Aisles.Order.ToList().IndexOf(i.Aisle)).ToList();
        Assert.Equal(aisles.OrderBy(x => x), aisles);
    }

    [Fact]
    public void Text_export_groups_by_aisle()
    {
        var calculator = TestData.Calculator();
        var lomo = calculator.Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients);
        var builder = new ShoppingListBuilder(new UnitConverter(), new FixedTime(DateTimeOffset.UnixEpoch));
        var list = builder.Build([lomo], [], _ingredients, ["ajo"]);

        var text = ShoppingListBuilder.ToText(list, "Lista de prueba");

        Assert.StartsWith("🛒 Lista de prueba", text);
        Assert.Contains("*Frutas y verduras*", text);
        Assert.Contains("- [ ] Lomo de cerdo: 1.15 kg", text);
        Assert.Contains("*Ya tienes (revisa que alcance)*", text);
        Assert.True(text.IndexOf("*Frutas y verduras*", StringComparison.Ordinal) < text.IndexOf("*Carnes y pescados*", StringComparison.Ordinal));
    }

    private static void AssertItem(ShoppingList list, string id, string display, string aisle)
    {
        var item = list.Items.SingleOrDefault(i => i.IngredientId == id);
        Assert.True(item is not null, $"Falta '{id}' en la lista.");
        Assert.Equal(display, item!.PurchaseDisplay);
        Assert.Equal(aisle, item.Aisle);
    }
}
