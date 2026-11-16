using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Bunit;
using MenuPlanner.Domain;
using MenuPlanner.Web.Components;
using MenuPlanner.Web.Pages;
using MenuPlanner.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MenuPlanner.WebTests;

internal static class Files
{
    public static string Schema(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", $"{name}.schema.json"));

    public static JsonObject SeedRecipe(string id) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed", "recipes.json")))!
            .AsArray().First(r => r!["id"]!.GetValue<string>() == id)!.DeepClone();
}

public class SchemaContextTests
{
    private static SchemaContext Context(string schema) => new((JsonObject)JsonNode.Parse(Files.Schema(schema))!);

    [Fact]
    public void Resolves_refs_and_keeps_local_titles()
    {
        var ctx = Context("nutrition-plan");
        var meals = (JsonObject)ctx.Root["properties"]!["meals"]!;
        var item = ctx.Describe((JsonObject)meals["items"]!);

        Assert.Equal(SchemaKind.Object, item.Kind);
        Assert.Equal("Tiempo de comida", SchemaContext.Title(item.Schema));
        Assert.Contains(ctx.Properties(item.Schema), p => p.Name == "components");
    }

    [Fact]
    public void Describes_nullable_types_enums_and_anyof()
    {
        var ctx = Context("ingredient");
        var props = (JsonObject)ctx.Root["properties"]!;

        var group = ctx.Describe((JsonObject)props["group"]!);       // anyOf [$ref enum, null]
        Assert.Equal(SchemaKind.Enum, group.Kind);
        Assert.True(group.Nullable);
        Assert.Contains("proteina", group.EnumValues);

        var notes = ctx.Describe((JsonObject)props["notes"]!);       // ["string","null"]
        Assert.Equal(SchemaKind.String, notes.Kind);
        Assert.True(notes.Nullable);

        Assert.Equal(SchemaKind.Number, ctx.Describe((JsonObject)props["cookedYield"]!).Kind);
        Assert.Equal(SchemaKind.Array, ctx.Describe((JsonObject)props["conversions"]!).Kind);
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("ingredient")]
    [InlineData("dish")]
    [InlineData("nutrition-plan")]
    public void Creates_defaults_with_required_properties(string schema)
    {
        var ctx = Context(schema);
        var document = Assert.IsType<JsonObject>(ctx.CreateDefault(ctx.Root));

        foreach (var required in SchemaContext.RequiredOf(ctx.Root))
        {
            Assert.True(document.ContainsKey(required), $"{schema}: falta {required}");
        }
    }
}

public class SchemaFormTests
{
    // MudBlazor solo se libera de forma asíncrona: un contexto por prueba con "await using".
    private static BunitContext NewContext()
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        return ctx;
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("ingredient")]
    [InlineData("dish")]
    [InlineData("nutrition-plan")]
    public async Task Renders_every_real_schema(string schema)
    {
        await using var bunit = NewContext();
        var ctx = new SchemaContext((JsonObject)JsonNode.Parse(Files.Schema(schema))!);
        var document = (JsonObject)ctx.CreateDefault(ctx.Root)!;

        var cut = bunit.Render<SchemaForm>(p => p
            .Add(x => x.SchemaText, Files.Schema(schema))
            .Add(x => x.Document, document));

        Assert.NotEmpty(cut.FindAll("[data-path]"));
        Assert.NotNull(cut.Find("[data-path='/id']"));
    }

    [Fact]
    public async Task Edits_validate_against_the_schema_and_report_errors_by_field()
    {
        await using var bunit = NewContext();
        var recipe = Files.SeedRecipe("pollo-teriyaki");
        bool? valid = null;
        var cut = bunit.Render<SchemaForm>(p => p
            .Add(x => x.SchemaText, Files.Schema("recipe"))
            .Add(x => x.Document, recipe)
            .Add(x => x.ValidityChanged, v => valid = v));

        Assert.True(cut.Instance.IsValid);
        Assert.True(valid);                                                     // validez inicial notificada
        Assert.NotNull(cut.Find("[data-path='/components/0/group']"));

        cut.Find("[data-path='/name']").Change(string.Empty);                  // minLength 1
        Assert.False(cut.Instance.IsValid);
        Assert.False(valid);
        Assert.Contains("/name", cut.Instance.Errors.Keys);
        Assert.NotNull(cut.Find("[data-testid=schema-errors]"));

        cut.Find("[data-path='/name']").Change("Pollo teriyaki ligero");
        Assert.True(cut.Instance.IsValid);
        Assert.Equal("Pollo teriyaki ligero", recipe["name"]!.GetValue<string>());
        Assert.Empty(cut.FindAll("[data-testid=schema-errors]"));
    }

    [Fact]
    public async Task Arrays_can_add_and_remove_items()
    {
        await using var bunit = NewContext();
        var recipe = Files.SeedRecipe("pollo-teriyaki");
        var cut = bunit.Render<SchemaForm>(p => p
            .Add(x => x.SchemaText, Files.Schema("recipe"))
            .Add(x => x.Document, recipe));
        var tags = recipe["tags"]!.AsArray().Count;

        var tagsBlock = cut.Find("[data-path='/tags']");
        tagsBlock.QuerySelectorAll("button").First(b => b.TextContent.Contains("Agregar")).Click();
        Assert.Equal(tags + 1, recipe["tags"]!.AsArray().Count);

        cut.Find("[data-path='/tags']").QuerySelectorAll("button[aria-label='Eliminar']").Last().Click();
        Assert.Equal(tags, recipe["tags"]!.AsArray().Count);
        Assert.True(cut.Instance.IsValid);
    }

    [Fact]
    public async Task Locked_properties_are_read_only()
    {
        await using var bunit = NewContext();
        var cut = bunit.Render<SchemaForm>(p => p
            .Add(x => x.SchemaText, Files.Schema("recipe"))
            .Add(x => x.Document, Files.SeedRecipe("alambre"))
            .Add(x => x.LockedProperties, ["id"]));

        Assert.True(cut.Find("[data-path='/id']").HasAttribute("readonly"));
        Assert.False(cut.Find("[data-path='/name']").HasAttribute("readonly"));
    }
}

public class ShoppingListPageTests
{
    private const string ListJson = """
        {
          "generatedAt": "2026-11-16T00:00:00Z",
          "items": [
            { "ingredientId": "pechuga-pollo", "name": "Pechuga de pollo", "aisle": "Carnes y pescados", "neededQuantity": 1120, "neededUnit": "g",
              "neededDisplay": "1.12 kg", "purchaseQuantity": 1150, "purchaseUnit": "g", "purchaseDisplay": "1.15 kg", "sources": ["Pollo teriyaki"] },
            { "ingredientId": "brocoli", "name": "Brócoli", "aisle": "Frutas y verduras", "neededQuantity": 360, "neededUnit": "g",
              "neededDisplay": "360 g", "purchaseQuantity": 400, "purchaseUnit": "g", "purchaseDisplay": "400 g", "sources": ["Pollo teriyaki"] }
          ],
          "alreadyHave": [
            { "ingredientId": "ajo", "name": "Ajo", "aisle": "Frutas y verduras", "neededQuantity": 2, "neededUnit": "diente",
              "neededDisplay": "2 dientes", "purchaseQuantity": 1, "purchaseUnit": "pieza", "purchaseDisplay": "1 cabeza", "sources": [] }
          ]
        }
        """;

    [Fact]
    public async Task Groups_by_aisle_and_remembers_checks_in_local_storage()
    {
        await using var bunit = new BunitContext();
        bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        bunit.Services.AddMudServices();
        bunit.Services.AddScoped(_ => new HttpClient(new FakeApi(ListJson)) { BaseAddress = new Uri("http://localhost/") });
        bunit.Services.AddScoped<ApiClient>();
        bunit.Services.AddScoped<BrowserStorage>();
        bunit.JSInterop.Setup<string?>("menuApp.getItem", "compras:menu-1").SetResult("brocoli");
        var setItem = bunit.JSInterop.SetupVoid("menuApp.setItem", _ => true);

        var cut = bunit.Render<ShoppingListPage>(p => p.Add(x => x.MenuId, "menu-1"));

        // La página carga la lista y las palomitas de forma asíncrona: se espera a que termine.
        cut.WaitForAssertion(() => Assert.Contains("1 de 2 artículos", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(2, cut.FindAll("[data-testid=aisle]").Count);
        Assert.Contains("1.15 kg", cut.Markup);
        Assert.Contains("Ya tienes", cut.Markup);
        Assert.Contains("1 de 2 artículos", cut.Markup);                       // brócoli venía palomeado

        cut.FindAll("input[type=checkbox]")[0].Change(true);                   // primer pasillo: Carnes → pechuga

        cut.WaitForAssertion(() => Assert.Contains("2 de 2 artículos", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains(setItem.Invocations, i => ((string)i.Arguments[1]!).Contains("pechuga-pollo"));
    }

    private sealed class FakeApi(string shoppingList) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            var body = url.Contains("format=text", StringComparison.Ordinal) ? "🛒 Lista" : shoppingList;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, url.Contains("format=text", StringComparison.Ordinal) ? "text/plain" : "application/json"),
            });
        }
    }
}

public class DishCardTests
{
    [Fact]
    public async Task Shows_approximate_macros_per_serving()
    {
        await using var bunit = new BunitContext();
        bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        bunit.Services.AddMudServices();
        var portioned = new PortionedRecipe
        {
            RecipeId = "pollo-teriyaki",
            Name = "Pollo teriyaki",
            Servings = 4,
            ProteinPerServingGrams = 200,
            Nutrition = new RecipeNutrition { PerServing = new NutritionFacts { Kcal = 577, Protein = 70.9, Carbs = 40, Fat = 6, Fiber = 5 } },
        };

        var cut = bunit.Render<DishCard>(p => p.Add(x => x.Dish, new MenuDishView("pollo-teriyaki", "Pollo teriyaki", 0, portioned)));

        Assert.Contains("≈ 577 kcal · 71 g prot.", cut.Find("[data-testid=kcal-chip]").TextContent);
    }
}
