using MenuPlanner.Domain;
using MenuPlanner.Portioning;

namespace MenuPlanner.UnitTests;

public class UnitConverterTests
{
    private readonly UnitConverter _converter = new();
    private readonly Dictionary<string, Ingredient> _ingredients = TestData.Ingredients();

    [Fact]
    public void Cooked_protein_is_converted_to_raw_with_yield()
    {
        // 4 porciones × 200 g de lomo cocido ÷ 0.70 de rendimiento
        var raw = _converter.ToBase(_ingredients["lomo-cerdo"], 800, Unit.G, FoodState.Cocido);
        Assert.Equal(1142.86, raw, 2);
    }

    [Fact]
    public void Cooked_cup_of_rice_is_converted_to_dry_grams()
    {
        var raw = _converter.ToBase(_ingredients["arroz"], 0.5, Unit.Taza, FoodState.Cocido);
        Assert.Equal(195 * 0.5 / 2.9, raw, 2);
    }

    [Fact]
    public void Countable_units_keep_the_count_when_cooked()
    {
        var raw = _converter.ToBase(_ingredients["papa"], 0.5, Unit.Pieza, FoodState.Cocido);
        Assert.Equal(85, raw, 2);
    }

    [Fact]
    public void Same_unit_as_base_is_not_converted()
    {
        Assert.Equal(2, _converter.ToBase(_ingredients["tortilla-maiz"], 2, Unit.Pieza, FoodState.Crudo));
    }

    [Fact]
    public void Converts_between_base_and_purchase_units()
    {
        Assert.Equal(240, _converter.Convert(_ingredients["tortilla-maiz"], 8, Unit.Pieza, Unit.G), 2);
        Assert.Equal(0.2, _converter.Convert(_ingredients["ajo"], 2, Unit.Diente, Unit.Pieza), 2);
    }

    [Fact]
    public void Missing_conversion_throws_a_clear_error()
    {
        var ex = Assert.Throws<ConversionException>(() => _converter.ToBase(_ingredients["lomo-cerdo"], 1, Unit.Taza, FoodState.Crudo));
        Assert.Contains("Lomo de cerdo", ex.Message);
    }
}

public class QuantityFormatterTests
{
    [Theory]
    [InlineData(1150, Unit.G, "1.15 kg")]
    [InlineData(600, Unit.G, "600 g")]
    [InlineData(1000, Unit.Ml, "1 L")]
    [InlineData(0.25, Unit.Pieza, "¼ pza")]
    [InlineData(8, Unit.Pieza, "8 pzas")]
    [InlineData(1.5, Unit.Taza, "1½ tz")]
    [InlineData(2, Unit.Diente, "2 dientes")]
    public void Formats_quantities(double value, Unit unit, string expected) =>
        Assert.Equal(expected, QuantityFormatter.Format(value, unit));

    [Theory]
    [InlineData(1142.857, Unit.G, 1150)]
    [InlineData(67.9, Unit.G, 70)]
    [InlineData(33.6, Unit.G, 34)]
    [InlineData(0.9, Unit.Pieza, 1)]
    [InlineData(8, Unit.Pieza, 8)]
    public void Rounds_up_for_cooking(double value, Unit unit, double expected) =>
        Assert.Equal(expected, QuantityFormatter.RoundForCooking(value, unit));
}

public class PortionCalculatorTests
{
    private readonly PortionCalculator _calculator = TestData.Calculator();
    private readonly NutritionPlan _plan = TestData.Plan();
    private readonly Dictionary<string, Ingredient> _ingredients = TestData.Ingredients();

    [Fact]
    public void Scales_lomo_en_salsa_verde_to_the_plan()
    {
        var result = _calculator.Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients);

        Assert.True(result.Compatible);
        Assert.Equal(200, result.ProteinPerServingGrams);
        Assert.Equal(1150, Line(result, "lomo-cerdo").Quantity);                 // 4 × 200 g cocido ÷ 0.70
        Assert.Equal("1.15 kg", Line(result, "lomo-cerdo").Display);
        Assert.Equal(600, Line(result, "nopal").Quantity);                       // 8 tz × ½ × 150 g
        Assert.Equal(280, Line(result, "champinon").Quantity);                   // 8 tz × ½ × 70 g
        Assert.Equal(8, Line(result, "tortilla-maiz").Quantity);                 // 4 × 2 tortillas
        Assert.DoesNotContain(result.Batch, l => l.IngredientId == "frijol");    // acompañamiento apagado por defecto
        Assert.Contains(result.Serving, s => s.Text == "200 g de lomo de cerdo (en cocido)");
        Assert.Contains(result.Serving, s => s.Text == "2 pzas de tortilla de maíz");
    }

    [Fact]
    public void Fixed_ingredients_are_not_scaled()
    {
        var target = TestData.Calculator().Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients,
            new PortionOptions { ProteinTarget = ProteinTarget.Min });

        var salsa = Line(target, "tomate-verde");
        Assert.False(salsa.Scaled);
        Assert.Equal(500, salsa.Quantity);
        Assert.Equal(860, Line(target, "lomo-cerdo").Quantity);                  // 4 × 150 ÷ 0.70 = 857 → 860
    }

    [Fact]
    public void Legume_side_applies_only_to_the_requested_servings()
    {
        var result = _calculator.Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients,
            new PortionOptions { LegumeSideServings = 2 });

        var frijol = Line(result, "frijol");
        Assert.Equal(2, frijol.Servings);
        Assert.Equal(70, frijol.Quantity);                                       // 2 × ½ tz cocido = 170 g cocido ÷ 2.5 = 68 → 70 g seco
        Assert.Contains(result.Serving, s => s.Group == ComponentGroup.Leguminosa && s.Text.Contains("en 2 de 4 porciones"));
    }

    [Fact]
    public void Exchange_portions_use_the_plan_table()
    {
        var result = _calculator.Calculate(TestData.Recipe("pollo-plancha-verduras-arroz"), _plan, _ingredients);

        Assert.Equal(135, Line(result, "arroz").Quantity);                       // 4 × ½ tz cocido = 390 g cocido ÷ 2.9 = 134.5 → 135 g crudo
        Assert.Contains(result.Serving, s => s.Text == "½ tz de arroz (en cocido)");
    }

    [Fact]
    public void Missing_vegetable_and_cereal_are_completed_with_suggested_sides()
    {
        var recipe = TestData.Recipe("lomo-salsa-verde-nopales");
        recipe.Components.RemoveAll(c => c.Group is ComponentGroup.Verdura or ComponentGroup.Cereal);

        var result = _calculator.Calculate(recipe, _plan, _ingredients);

        var side = Line(result, "verdura-mixta");
        Assert.True(side.Suggested);
        Assert.Equal(800, side.Quantity);                                        // 8 tz × 100 g
        Assert.True(Line(result, "tortilla-maiz").Suggested);                    // primera opción de la tabla de cereal
        Assert.Equal(2, result.Suggestions.Count);
    }

    [Fact]
    public void Forbidden_cooking_method_makes_the_recipe_incompatible()
    {
        var recipe = TestData.Recipe("pollo-plancha-verduras-arroz");
        recipe.CookingMethod = "empanizado";

        var result = _calculator.Calculate(recipe, _plan, _ingredients);

        Assert.False(result.Compatible);
        Assert.Contains(result.Warnings, w => w.Contains("empanizado"));
    }

    [Fact]
    public void Cereal_outside_the_exchange_table_is_reported()
    {
        var recipe = TestData.Recipe("pollo-plancha-verduras-arroz");
        recipe.GetComponent(ComponentGroup.Cereal)!.Ingredients[0].IngredientId = "frijol";

        var result = _calculator.Calculate(recipe, _plan, _ingredients);

        Assert.Contains(result.Warnings, w => w.Contains("tabla de intercambios"));
    }

    [Fact]
    public void Non_lean_protein_is_warned()
    {
        _ingredients["lomo-cerdo"].IsLean = false;

        var result = _calculator.Calculate(TestData.Recipe("lomo-salsa-verde-nopales"), _plan, _ingredients);

        Assert.Contains(result.Warnings, w => w.Contains("magra"));
    }

    private static PortionLine Line(PortionedRecipe result, string ingredientId) =>
        result.Batch.Single(l => l.IngredientId == ingredientId);
}
