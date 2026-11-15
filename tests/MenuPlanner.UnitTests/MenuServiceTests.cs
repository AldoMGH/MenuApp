using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Domain;

namespace MenuPlanner.UnitTests;

/// <summary>Flujo completo de casos de uso con repositorios en memoria.</summary>
public class MenuServiceTests
{
    [Fact]
    public async Task Generates_a_weekly_menu_with_five_dishes()
    {
        var store = InMemoryStore.FromSeed();
        var service = store.CreateService();

        var menu = await service.GenerateAsync(new GenerateMenuRequest(5, Seed: 123));

        Assert.Equal(MenuStatus.Draft, menu.Status);
        Assert.Equal(new DateOnly(2026, 11, 16), menu.WeekOf);            // siguiente lunes
        Assert.Equal(5, menu.Dishes.Count);
        Assert.Equal(20, menu.Summary.TotalServings);
        Assert.Equal(13, menu.Summary.SurplusServings);
        Assert.InRange(menu.Summary.LegumeServings, menu.Summary.LegumeMinServings, menu.Summary.LegumeMaxServings);
        Assert.All(menu.Dishes, d => Assert.Equal(4, d.Portioned.Servings));
        Assert.Single(store.Menus);
    }

    [Fact]
    public async Task Text_summary_reads_like_the_example()
    {
        var service = InMemoryStore.FromSeed().CreateService();
        var menu = await service.GenerateAsync(new GenerateMenuRequest(5, Seed: 123));

        var text = MenuTextFormatter.Format(menu);

        Assert.Contains("Menú semana del 16 de noviembre", text);
        Assert.Contains("Tu porción:", text);
        Assert.Contains("Leguminosa:", text);
        Assert.Contains("sobran 13", text);
    }

    [Fact]
    public async Task Add_remove_and_swap_dishes()
    {
        var store = InMemoryStore.FromSeed();
        var service = store.CreateService();
        var menu = await service.GenerateAsync(new GenerateMenuRequest(3, Seed: 5, Exclude: ["tinga-pollo"]));

        menu = await service.AddDishAsync(menu.Id, "tinga-pollo");
        Assert.Equal(4, menu.Dishes.Count);
        await Assert.ThrowsAsync<ConflictException>(() => service.AddDishAsync(menu.Id, "tinga-pollo"));

        menu = await service.RemoveDishAsync(menu.Id, "tinga-pollo");
        Assert.Equal(3, menu.Dishes.Count);

        var first = menu.Dishes[0].RecipeId;
        menu = await service.SwapDishAsync(menu.Id, first, new SwapCriteria(ProteinFamily: "cerdo"));
        Assert.Equal("lomo-salsa-verde-nopales", menu.Dishes[0].RecipeId);
        Assert.Equal(3, menu.Dishes.Select(d => d.RecipeId).Distinct().Count());
    }

    [Fact]
    public async Task Confirm_freezes_the_menu_and_updates_history()
    {
        var store = InMemoryStore.FromSeed();
        store.Pantry = new Pantry { IngredientIds = ["ajo", "aceite-oliva"] };
        var service = store.CreateService();
        var menu = await service.GenerateAsync(new GenerateMenuRequest(4, Seed: 9));

        var confirmed = await service.ConfirmAsync(menu.Id);

        Assert.Equal(MenuStatus.Confirmed, confirmed.Status);
        var saved = store.Menus.Single();
        Assert.NotNull(saved.ShoppingList);
        Assert.All(saved.Dishes, d => Assert.NotNull(d.Snapshot));
        Assert.All(menu.Dishes, d =>
        {
            var recipe = store.RecipeList.Single(r => r.Id == d.RecipeId);
            Assert.Equal(1, recipe.Stats.TimesCooked);
            Assert.Equal(new DateTime(2026, 11, 16), recipe.Stats.LastCooked!.Value.Date);
        });

        var list = await service.GetShoppingListAsync(menu.Id);
        Assert.DoesNotContain(list.Items, i => i.IngredientId == "ajo");
        Assert.Contains(list.Items, i => i.IngredientId == "fruta-temporada");     // básicos de colación

        await Assert.ThrowsAsync<ConflictException>(() => service.AddDishAsync(menu.Id, "tinga-pollo"));
    }

    [Fact]
    public async Task Confirmed_menu_keeps_its_snapshot_when_the_plan_changes()
    {
        var store = InMemoryStore.FromSeed();
        var service = store.CreateService();
        var menu = await service.GenerateAsync(new GenerateMenuRequest(2, Seed: 3));
        await service.ConfirmAsync(menu.Id);

        var v3 = TestData.Clone(store.Plans.Single());
        v3.Id = "plan-3";
        v3.Version = 3;
        v3.Meals[0].GetSlot(ComponentGroup.Proteina)!.Max = 180;
        store.Plans.Single().Active = false;
        store.Plans.Add(v3);

        var old = await service.GetAsync(menu.Id);
        var fresh = await service.GenerateAsync(new GenerateMenuRequest(2, Seed: 3));

        Assert.All(old.Dishes, d => Assert.Equal(200, d.Portioned.ProteinPerServingGrams));
        Assert.Equal(2, old.PlanVersion);
        Assert.All(fresh.Dishes, d => Assert.Equal(180, d.Portioned.ProteinPerServingGrams));
        Assert.Equal(3, fresh.PlanVersion);
    }

    [Fact]
    public async Task Rejects_invalid_requests()
    {
        var store = InMemoryStore.FromSeed();
        var service = store.CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() => service.GenerateAsync(new GenerateMenuRequest(0)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync("no-existe"));

        store.Plans.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => service.GenerateAsync(new GenerateMenuRequest(5)));
    }
}
