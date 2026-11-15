using System.Text.Json.Nodes;
using MenuPlanner.Application.Catalog;
using MenuPlanner.Application.Validation;

namespace MenuPlanner.UnitTests;

public class SeedAndSchemaTests
{
    [Fact]
    public void Every_seed_document_matches_its_schema()
    {
        var validator = TestData.Schemas();
        var seed = TestData.Load();

        Assert.NotEmpty(seed.RawDocuments);
        foreach (var (source, schema, node) in seed.RawDocuments)
        {
            var errors = validator.Validate(schema, node);
            Assert.True(errors.Count == 0, $"{source}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void Seed_references_are_consistent()
    {
        var seed = TestData.Load();
        var ingredients = TestData.Ingredients();
        var plan = TestData.Plan();

        Assert.Single(seed.Plans, p => p.Active);
        Assert.Empty(CatalogValidator.ValidatePlan(plan, ingredients));
        foreach (var recipe in seed.Recipes)
        {
            var errors = CatalogValidator.ValidateRecipe(recipe, ingredients, plan);
            Assert.True(errors.Count == 0, $"{recipe.Id}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void Every_seed_recipe_portions_cleanly()
    {
        var calculator = TestData.Calculator();
        var plan = TestData.Plan();
        var ingredients = TestData.Ingredients();

        foreach (var recipe in TestData.Recipes())
        {
            var result = calculator.Calculate(recipe, plan, ingredients, new() { LegumeSideServings = 4 });
            Assert.True(result.Compatible, recipe.Id);
            Assert.True(result.Warnings.Count == 0, $"{recipe.Id}: {string.Join("; ", result.Warnings)}");
            Assert.True(result.Suggestions.Count == 0, $"{recipe.Id}: {string.Join("; ", result.Suggestions)}");
            Assert.Equal(200, result.ProteinPerServingGrams);
        }
    }

    [Fact]
    public void Schema_rejects_invalid_recipes()
    {
        var validator = TestData.Schemas();
        var recipe = JsonNode.Parse("""
            {
              "id": "Mala Receta",
              "meal": "comida",
              "servings": 4,
              "cookingMethod": "guisado",
              "components": [ { "group": "proteina", "ingredients": [] } ],
              "fixedIngredients": [ { "ingredientId": "ajo", "qty": 1, "unit": "kilo" } ]
            }
            """);

        var errors = validator.Validate(SchemaNames.Recipe, recipe);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase) || e.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Schemas_are_available_for_dynamic_forms()
    {
        var validator = TestData.Schemas();

        Assert.Contains(SchemaNames.NutritionPlan, validator.Names);
        Assert.Contains(SchemaNames.Recipe, validator.Names);
        Assert.Contains(SchemaNames.Ingredient, validator.Names);
        Assert.Contains("\"title\": \"Receta\"", validator.GetRaw(SchemaNames.Recipe));
    }
}
