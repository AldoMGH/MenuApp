using MenuPlanner.Application.Imports;
using MenuPlanner.Application.Menus;
using MenuPlanner.Infrastructure.Memory;
using MenuPlanner.Mcp.Tools;
using MenuPlanner.UnitTests;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MenuPlanner.McpTests;

internal sealed class ToolHarness
{
    public ToolHarness()
    {
        Store = TestData.Store();
        var menus = Store.CreateService();
        var imports = Store.CreateImportService();
        Resolver = new DishResolver(Store, Store, Store);
        Menu = new MenuTools(menus, Resolver);
        Recipes = new RecipeTools(menus, Resolver, Store, new DishService(Store, Store, imports), imports);
        Imports = imports;
    }

    public InMemoryStore Store { get; }

    public DishResolver Resolver { get; }

    public MenuTools Menu { get; }

    public RecipeTools Recipes { get; }

    public ImportService Imports { get; }
}

public class DishResolverTests
{
    private readonly ToolHarness _h = new();

    [Theory]
    [InlineData("enchiladas", "enchiladas-suizas")]
    [InlineData("las enchiladas suizas", "enchiladas-suizas")]
    [InlineData("Poyo ajonjolí", "pollo-ajonjoli")]
    [InlineData("tinga", "tinga-pollo")]
    [InlineData("puerco en salsa verde", "lomo-salsa-verde-nopales")]
    [InlineData("lomo-salsa-verde-nopales", "lomo-salsa-verde-nopales")]
    [InlineData("Pollo teriyaky", "pollo-teriyaki")]
    public async Task Resolves_how_you_talk(string query, string recipeId)
    {
        var result = await _h.Resolver.ResolveAsync(query);
        Assert.Equal(recipeId, result.Recipe?.RecipeId);
    }

    [Fact]
    public async Task Dish_without_recipe_is_explained()
    {
        var result = await _h.Resolver.ResolveAsync("milanesas");
        Assert.False(result.Found);
        Assert.Contains("no tiene receta", result.Message);
    }

    [Fact]
    public async Task Ambiguous_names_return_options()
    {
        var result = await _h.Resolver.ResolveAsync("pescado");
        Assert.False(result.Found);
        Assert.True(result.Candidates.Count >= 2);
    }

    [Fact]
    public async Task Can_be_limited_to_a_menu()
    {
        var result = await _h.Resolver.ResolveAsync("alambre", ["pollo-teriyaki"]);
        Assert.False(result.Found);
        Assert.Contains("no está en el menú", result.Message);
    }
}

public class McpToolTests
{
    private readonly ToolHarness _h = new();

    [Fact]
    public async Task Generate_swap_confirm_and_shopping_list()
    {
        var text = await _h.Menu.GenerateWeeklyMenu(5, "2026-11-23", include: ["enchiladas suizas"], exclude: ["pollo teriyaki"], seed: 3);
        Assert.Contains("Menú semana del 23 de noviembre", text);
        Assert.Contains("Enchiladas suizas", text);
        Assert.Contains("Tu porción:", text);
        var menuId = _h.Store.Menus.Single().Id;
        Assert.Contains($"Id del menú: {menuId}", text);

        var swapped = await _h.Menu.SwapDish("enchiladas", proteinFamily: "pescado");
        Assert.StartsWith("Se cambió 'Enchiladas suizas", swapped);
        Assert.DoesNotContain("enchiladas-suizas", _h.Store.Menus.Single().Dishes.Select(d => d.RecipeId));

        var added = await _h.Menu.AddDish("pollo teriyaki", menuId);
        Assert.Contains("Se agregó 'Pollo teriyaki", added);

        var removed = await _h.Menu.RemoveDish("teriyaki");
        Assert.Contains("Se quitó", removed);

        var list = await _h.Menu.GetShoppingList();
        Assert.Contains("🛒 Lista de compras", list);
        Assert.Contains("*Carnes y pescados*", list);

        var confirmed = await _h.Menu.ConfirmMenu();
        Assert.Contains("Menú confirmado.", confirmed);
        Assert.Contains("confirmado", await _h.Menu.ListMenus());

        Assert.StartsWith("⚠️", await _h.Menu.AddDish("alambre", menuId));            // ya confirmado
    }

    [Fact]
    public async Task Errors_come_back_as_friendly_text()
    {
        Assert.StartsWith("⚠️", await _h.Menu.GetMenu());                             // aún no hay menús
        Assert.StartsWith("⚠️", await _h.Menu.GenerateWeeklyMenu(0));
        Assert.StartsWith("⚠️", await _h.Menu.GenerateWeeklyMenu(3, weekOf: "la otra semana"));

        var text = await _h.Menu.GenerateWeeklyMenu(3, include: ["milanesas"], seed: 1);
        Assert.Contains("No se pudo usar 'milanesas'", text);
    }

    [Fact]
    public async Task Recipe_tools()
    {
        var recipe = await _h.Recipes.GetRecipe("lomo en salsa verde", "min");
        Assert.Contains("Para cocinar (lote):", recipe);
        Assert.Contains("860 g lomo de cerdo", recipe);
        Assert.Contains("150 g de lomo de cerdo (en cocido)", recipe);
        Assert.Contains("Pasos:", recipe);

        Assert.Contains("revisarla con la nutrióloga", await _h.Recipes.GetRecipe("boneless"));
        Assert.Contains("Pollo teriyaki", await _h.Recipes.ListRecipes("asiatica"));
        Assert.Contains("historial", await _h.Recipes.ListFavorites());                 // sin import todavía

        await _h.Imports.ImportKeepAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keep-sample-drive.md")), null, null, false);
        Assert.Contains("Enchiladas suizas: 2 veces", await _h.Recipes.ListFavorites());
        Assert.Contains("Pierna de cerdo", await _h.Recipes.PantrySuggestions(12));
    }
}

/// <summary>De punta a punta: lanza el servidor compilado por stdio (modo memoria) y lo usa con el cliente oficial del SDK.</summary>
public class McpStdioTests
{
    [Fact]
    public async Task Claude_desktop_style_session()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "MenuPlanner.Mcp.dll");
        Assert.True(File.Exists(dll), dll);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "menu-planner",
            Command = "dotnet",
            Arguments = [dll],
            WorkingDirectory = Path.GetTempPath(),
            EnvironmentVariables = new Dictionary<string, string?> { ["Storage__Provider"] = "Memory" },
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        Assert.Contains(tools, t => t.Name == "generate_weekly_menu");
        Assert.Contains(tools, t => t.Name == "get_shopping_list");
        Assert.Equal(12, tools.Count);

        var result = await client.CallToolAsync(
            "generate_weekly_menu",
            new Dictionary<string, object?> { ["dishCount"] = 5, ["seed"] = 7 },
            cancellationToken: cts.Token);

        Assert.NotEqual(true, result.IsError);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Leguminosa:", text);
        Assert.Contains("Id del menú:", text);

        var list = await client.CallToolAsync("get_shopping_list", new Dictionary<string, object?>(), cancellationToken: cts.Token);
        Assert.Contains("Lista de compras", string.Concat(list.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }
}
