using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Domain;
using MenuPlanner.Infrastructure.Memory;
using MenuPlanner.UnitTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MenuPlanner.ApiTests;

/// <summary>API real (rutas, JSON, schemas, errores) sobre repositorios en memoria, sin MongoDB.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public InMemoryStore Store { get; } = TestData.Store();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPlanRepository>();
            services.RemoveAll<IIngredientRepository>();
            services.RemoveAll<IRecipeRepository>();
            services.RemoveAll<IMenuRepository>();
            services.RemoveAll<ISettingsRepository>();
            services.RemoveAll<IDishRepository>();
            services.RemoveAll<IImportRepository>();
            services.AddSingleton<IDishRepository>(Store);
            services.AddSingleton<IImportRepository>(Store);
            services.AddSingleton<IPlanRepository>(Store);
            services.AddSingleton<IIngredientRepository>(Store);
            services.AddSingleton<IRecipeRepository>(Store);
            services.AddSingleton<IMenuRepository>(Store);
            services.AddSingleton<ISettingsRepository>(Store);
        });
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = AppJson.Options;

    [Fact]
    public async Task Generate_menu_get_text_and_shopping_list()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/menus/generate", new { dishCount = 5, weekOf = "2026-11-16", seed = 123 }, Json);
        response.EnsureSuccessStatusCode();
        var menu = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        var id = menu!["id"]!.GetValue<string>();

        Assert.Equal("draft", menu["status"]!.GetValue<string>());
        Assert.Equal(5, menu["dishes"]!.AsArray().Count);
        Assert.Equal(20, menu["summary"]!["totalServings"]!.GetValue<int>());

        var text = await client.GetStringAsync($"/api/menus/{id}?format=text");
        Assert.Contains("Menú semana del 16 de noviembre", text);

        var list = await client.GetFromJsonAsync<ShoppingList>($"/api/menus/{id}/shopping-list", Json);
        Assert.Contains(list!.Items, i => i.IngredientId == "fruta-temporada");

        var listText = await client.GetStringAsync($"/api/menus/{id}/shopping-list?format=text");
        Assert.Contains("*Carnes y pescados*", listText);
    }

    [Fact]
    public async Task Add_remove_swap_and_confirm_through_http()
    {
        var client = factory.CreateClient();
        var menu = await Generate(client, 3, exclude: ["tinga-pollo"]);
        var id = menu["id"]!.GetValue<string>();

        var added = await client.PostAsJsonAsync($"/api/menus/{id}/dishes", new { recipeId = "tinga-pollo" }, Json);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var duplicate = await client.PostAsJsonAsync($"/api/menus/{id}/dishes", new { recipeId = "tinga-pollo" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var removed = await client.DeleteAsync($"/api/menus/{id}/dishes/tinga-pollo");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var first = menu["dishes"]![0]!["recipeId"]!.GetValue<string>();
        var swapped = await client.PostAsJsonAsync($"/api/menus/{id}/dishes/{first}/swap", new { }, Json);
        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);

        var confirmed = await client.PostAsync($"/api/menus/{id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        var afterConfirm = await client.PostAsJsonAsync($"/api/menus/{id}/dishes", new { recipeId = "tinga-pollo" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, afterConfirm.StatusCode);
    }

    [Fact]
    public async Task Portioned_recipe_endpoint_uses_the_active_plan()
    {
        var client = factory.CreateClient();

        var portioned = await client.GetFromJsonAsync<PortionedRecipe>(
            "/api/recipes/lomo-salsa-verde-nopales/portioned?proteinTarget=min&legumeSideServings=4", Json);

        Assert.Equal(150, portioned!.ProteinPerServingGrams);
        Assert.Contains(portioned.Batch, l => l.IngredientId == "frijol" && l.Servings == 4);
    }

    [Fact]
    public async Task Put_recipe_is_validated_against_schema_and_catalog()
    {
        var client = factory.CreateClient();

        var invalid = await client.PutAsJsonAsync("/api/recipes/receta-mala", new { name = "Mala", meal = "comida" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotEmpty(problem!["errors"]!.AsArray());

        var unknownIngredient = Recipe("receta-x", "ingrediente-que-no-existe");
        var badRef = await client.PutAsJsonAsync("/api/recipes/receta-x", unknownIngredient, Json);
        Assert.Equal(HttpStatusCode.BadRequest, badRef.StatusCode);

        var ok = await client.PutAsJsonAsync("/api/recipes/pollo-limon", Recipe("pollo-limon", "pechuga-pollo"), Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains(factory.Store.RecipeList, r => r.Id == "pollo-limon");
    }

    [Fact]
    public async Task Schemas_and_errors_are_exposed()
    {
        var client = factory.CreateClient();

        var schema = await client.GetStringAsync("/api/schemas/recipe");
        Assert.Contains("\"title\": \"Receta\"", schema);

        var missing = await client.GetAsync("/api/menus/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var badCount = await client.PostAsJsonAsync("/api/menus/generate", new { dishCount = 0 }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, badCount.StatusCode);
    }

    [Fact]
    public async Task Pantry_rejects_unknown_ingredients()
    {
        var client = factory.CreateClient();

        var bad = await client.PutAsJsonAsync("/api/pantry", new { ingredientIds = new[] { "no-existe" } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await client.PutAsJsonAsync("/api/pantry", new { ingredientIds = new[] { "aceite-oliva" } }, Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Cookable_recipes_and_macros_are_exposed()
    {
        var client = factory.CreateClient();
        await client.PutAsJsonAsync("/api/pantry", new { ingredientIds = new[] { "pechuga-pollo", "arroz", "brocoli", "zanahoria", "pimiento" } }, Json);

        var cookable = await client.GetFromJsonAsync<List<CookableRecipe>>("/api/recipes/cookable?limit=3", Json);
        Assert.Equal(3, cookable!.Count);
        Assert.Contains(cookable, c => c.RecipeId == "pollo-teriyaki");
        Assert.Equal(cookable.OrderByDescending(c => c.Coverage).Select(c => c.Coverage), cookable.Select(c => c.Coverage));
        Assert.All(cookable, c => Assert.NotNull(c.KcalPerServing));

        var portioned = await client.GetFromJsonAsync<PortionedRecipe>("/api/recipes/pollo-teriyaki/portioned", Json);
        Assert.Equal(577, portioned!.Nutrition!.PerServing.Kcal, 0);
    }

    private static async Task<JsonObject> Generate(HttpClient client, int count, string[] exclude)
    {
        var response = await client.PostAsJsonAsync("/api/menus/generate", new { dishCount = count, seed = 5, exclude }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static object Recipe(string id, string proteinId) => new
    {
        name = "Pollo al limón",
        meal = "comida",
        servings = 4,
        cookingMethod = "plancha",
        components = new object[]
        {
            new { group = "proteina", ingredients = new[] { new { ingredientId = proteinId } } },
            new { group = "verdura", ingredients = new[] { new { ingredientId = "brocoli" } } },
            new { group = "cereal", ingredients = new[] { new { ingredientId = "arroz" } } },
        },
        fixedIngredients = new[] { new { ingredientId = "limon", qty = 2, unit = "pieza" } },
    };
}
