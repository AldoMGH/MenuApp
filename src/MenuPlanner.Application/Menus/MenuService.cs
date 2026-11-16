using System.Globalization;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Domain;
using MenuPlanner.Portioning;

namespace MenuPlanner.Application.Menus;

/// <summary>Casos de uso del menú semanal: generar, agregar, quitar, cambiar, confirmar y lista de compras.</summary>
public sealed class MenuService(
    IPlanRepository plans,
    IRecipeRepository recipes,
    IIngredientRepository ingredients,
    IMenuRepository menus,
    ISettingsRepository settings,
    PortionCalculator calculator,
    MenuGenerator generator,
    ShoppingListBuilder shoppingBuilder,
    TimeProvider time,
    GenerationOptions? generationOptions = null)
{
    public const int MaxDishes = 14;

    private GenerationOptions Options { get; } = generationOptions ?? new GenerationOptions();

    public async Task<MenuView> GenerateAsync(GenerateMenuRequest request, CancellationToken ct = default)
    {
        if (request.DishCount is < 1 or > MaxDishes)
        {
            throw new BadRequestException($"dishCount debe estar entre 1 y {MaxDishes}.");
        }

        var context = await LoadContextAsync(ct);
        var weekOf = request.WeekOf ?? NextMonday(DateOnly.FromDateTime(time.GetLocalNow().Date));
        var weekOfDate = weekOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var seed = request.Seed ?? Random.Shared.Next(1, int.MaxValue);
        var options = OptionsFor(context.Plan, request.DishCount);

        var selection = generator.Select(
            new GenerationRequest(
                request.DishCount,
                weekOfDate,
                seed,
                request.MustInclude ?? [],
                request.Exclude ?? [],
                options),
            BuildCandidates(context));

        var now = time.GetUtcNow().UtcDateTime;
        var menu = new WeeklyMenu
        {
            Id = $"{weekOf:yyyy-MM-dd}-{Guid.NewGuid().ToString("N")[..6]}",
            WeekOf = weekOfDate,
            Status = MenuStatus.Draft,
            Seed = seed,
            PlanId = context.Plan.Id,
            PlanVersion = context.Plan.Version,
            Dishes = selection.Selected.Select(c => new MenuDish { RecipeId = c.Id, Name = c.Recipe.Name }).ToList(),
            GenerationWarnings = [.. selection.Warnings],
            CreatedAt = now,
            UpdatedAt = now,
        };

        Rebalance(menu, context);
        await menus.UpsertAsync(menu, ct);
        return BuildView(menu, context);
    }

    public async Task<MenuView> GetAsync(string id, CancellationToken ct = default)
    {
        var menu = await RequireMenuAsync(id, ct);
        var context = await LoadContextAsync(ct);
        return BuildView(menu, context);
    }

    public async Task<List<MenuListItem>> ListAsync(int limit = 20, CancellationToken ct = default)
    {
        var list = await menus.ListAsync(limit, ct);
        return list
            .Select(m => new MenuListItem(m.Id, DateOnly.FromDateTime(m.WeekOf), m.Status, m.Dishes.Select(d => d.Name).ToList()))
            .ToList();
    }

    public async Task<MenuView> AddDishAsync(string id, string recipeId, CancellationToken ct = default)
    {
        var menu = await RequireDraftAsync(id, ct);
        var context = await LoadContextAsync(ct);
        var recipe = context.Recipes.FirstOrDefault(r => r.Id == recipeId)
            ?? throw new NotFoundException($"No existe la receta '{recipeId}'.");

        if (menu.Dishes.Any(d => d.RecipeId == recipeId))
        {
            throw new ConflictException($"'{recipe.Name}' ya está en el menú.");
        }

        if (menu.Dishes.Count >= MaxDishes)
        {
            throw new BadRequestException($"El menú ya tiene {MaxDishes} platillos.");
        }

        menu.Dishes.Add(new MenuDish { RecipeId = recipe.Id, Name = recipe.Name });
        return await SaveAsync(menu, context, ct);
    }

    public async Task<MenuView> RemoveDishAsync(string id, string recipeId, CancellationToken ct = default)
    {
        var menu = await RequireDraftAsync(id, ct);
        var removed = menu.Dishes.RemoveAll(d => d.RecipeId == recipeId);
        if (removed == 0)
        {
            throw new NotFoundException($"La receta '{recipeId}' no está en el menú.");
        }

        var context = await LoadContextAsync(ct);
        return await SaveAsync(menu, context, ct);
    }

    public async Task<MenuView> SwapDishAsync(string id, string recipeId, SwapCriteria? criteria, CancellationToken ct = default)
    {
        var menu = await RequireDraftAsync(id, ct);
        var index = menu.Dishes.FindIndex(d => d.RecipeId == recipeId);
        if (index < 0)
        {
            throw new NotFoundException($"La receta '{recipeId}' no está en el menú.");
        }

        var context = await LoadContextAsync(ct);
        var all = BuildCandidates(context);
        var inMenu = menu.Dishes.Select(d => d.RecipeId).ToHashSet(StringComparer.Ordinal);
        var others = all.Where(c => inMenu.Contains(c.Id) && c.Id != recipeId).ToList();

        var pool = all.Where(c => !inMenu.Contains(c.Id)).ToList();
        if (!string.IsNullOrWhiteSpace(criteria?.ProteinFamily))
        {
            pool = pool.Where(c => string.Equals(c.ProteinFamily, criteria.ProteinFamily, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(criteria?.Tag))
        {
            pool = pool.Where(c => c.Recipe.Tags.Contains(criteria.Tag, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        menu.SwapCount++;
        var replacement = generator.PickReplacement(
            others,
            pool,
            menu.WeekOf,
            unchecked(menu.Seed + (menu.SwapCount * 7919)),
            OptionsFor(context.Plan, menu.Dishes.Count))
            ?? throw new BadRequestException("No hay otro platillo compatible que cumpla ese criterio.");

        menu.Dishes[index] = new MenuDish { RecipeId = replacement.Id, Name = replacement.Recipe.Name };
        return await SaveAsync(menu, context, ct);
    }

    public async Task<MenuView> ConfirmAsync(string id, CancellationToken ct = default)
    {
        var menu = await RequireDraftAsync(id, ct);
        if (menu.Dishes.Count == 0)
        {
            throw new BadRequestException("No se puede confirmar un menú sin platillos.");
        }

        var context = await LoadContextAsync(ct);
        Rebalance(menu, context);

        var portioned = new List<PortionedRecipe>();
        foreach (var dish in menu.Dishes)
        {
            var recipe = context.Recipes.FirstOrDefault(r => r.Id == dish.RecipeId)
                ?? throw new ConflictException($"La receta '{dish.RecipeId}' ya no existe; quítala del menú antes de confirmar.");
            dish.Snapshot = Portion(recipe, dish, context);
            portioned.Add(dish.Snapshot);
        }

        menu.ShoppingList = BuildShoppingList(portioned, context);
        menu.Status = MenuStatus.Confirmed;
        menu.PlanId = context.Plan.Id;
        menu.PlanVersion = context.Plan.Version;
        menu.ConfirmedAt = time.GetUtcNow().UtcDateTime;
        menu.UpdatedAt = menu.ConfirmedAt.Value;

        await menus.UpsertAsync(menu, ct);
        await recipes.RegisterCookedAsync(menu.Dishes.Select(d => d.RecipeId), menu.WeekOf, ct);
        return BuildView(menu, context);
    }

    public async Task<ShoppingList> GetShoppingListAsync(string id, CancellationToken ct = default)
    {
        var menu = await RequireMenuAsync(id, ct);
        if (menu.Status == MenuStatus.Confirmed && menu.ShoppingList is not null)
        {
            return menu.ShoppingList;
        }

        var context = await LoadContextAsync(ct);
        var view = BuildView(menu, context);
        return BuildShoppingList(view.Dishes.Select(d => d.Portioned), context);
    }

    public async Task<PortionedRecipe> PortionRecipeAsync(string recipeId, ProteinTarget? target, int? legumeSideServings, CancellationToken ct = default)
    {
        var context = await LoadContextAsync(ct);
        var recipe = context.Recipes.FirstOrDefault(r => r.Id == recipeId)
            ?? throw new NotFoundException($"No existe la receta '{recipeId}'.");

        return calculator.Calculate(recipe, context.Plan, context.Ingredients, new PortionOptions
        {
            ProteinTarget = target,
            LegumeSideServings = legumeSideServings ?? recipe.Servings,
        });
    }

    private async Task<MenuView> SaveAsync(WeeklyMenu menu, MenuContext context, CancellationToken ct)
    {
        Rebalance(menu, context);
        menu.PlanId = context.Plan.Id;
        menu.PlanVersion = context.Plan.Version;
        menu.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await menus.UpsertAsync(menu, ct);
        return BuildView(menu, context);
    }

    private ShoppingList BuildShoppingList(IEnumerable<PortionedRecipe> portioned, MenuContext context) =>
        shoppingBuilder.Build(
            portioned,
            context.Plan.WeeklyBasics.Concat(context.Basics.Items),
            context.Ingredients,
            context.Pantry.IngredientIds);

    private MenuView BuildView(WeeklyMenu menu, MenuContext context)
    {
        var dishes = new List<MenuDishView>();
        var warnings = new List<string>(menu.GenerationWarnings);

        foreach (var dish in menu.Dishes)
        {
            PortionedRecipe? portioned = dish.Snapshot;
            if (portioned is null)
            {
                var recipe = context.Recipes.FirstOrDefault(r => r.Id == dish.RecipeId);
                if (recipe is null)
                {
                    warnings.Add($"La receta '{dish.Name}' ({dish.RecipeId}) ya no existe en el catálogo.");
                    continue;
                }

                portioned = Portion(recipe, dish, context);
            }

            warnings.AddRange(portioned.Warnings.Select(w => $"{portioned.Name}: {w}"));
            dishes.Add(new MenuDishView(dish.RecipeId, dish.Name, dish.LegumeSideServings, portioned));
        }

        var balance = Balance(menu, context);
        warnings.AddRange(balance.Warnings);

        var mealsPerWeek = context.Plan.MealsPerWeek;
        var surplus = Math.Max(0, balance.TotalServings - mealsPerWeek);
        if (balance.TotalServings > 0 && balance.TotalServings < mealsPerWeek)
        {
            warnings.Add($"El menú cubre {balance.TotalServings} de {mealsPerWeek} comidas de la semana.");
        }

        var summary = new MenuSummary(
            dishes.Count,
            balance.TotalServings,
            mealsPerWeek,
            surplus,
            balance.LegumeServings,
            balance.MinServings,
            balance.MaxServings,
            balance.MealsPerWeekEquivalent,
            warnings);

        return new MenuView(
            menu.Id,
            DateOnly.FromDateTime(menu.WeekOf),
            menu.Status,
            menu.Seed,
            menu.PlanId,
            menu.PlanVersion,
            dishes,
            summary);
    }

    private PortionedRecipe Portion(Recipe recipe, MenuDish dish, MenuContext context) =>
        calculator.Calculate(recipe, context.Plan, context.Ingredients, new PortionOptions { LegumeSideServings = dish.LegumeSideServings });

    private static void Rebalance(WeeklyMenu menu, MenuContext context)
    {
        var balance = Balance(menu, context);
        foreach (var dish in menu.Dishes)
        {
            dish.LegumeSideServings = balance.SideServings.GetValueOrDefault(dish.RecipeId);
        }
    }

    private static LegumeBalance Balance(WeeklyMenu menu, MenuContext context)
    {
        var frequency = context.Plan.GetMeal(Meals.Comida)?.GetSlot(ComponentGroup.Leguminosa)?.WeeklyFrequency;
        var mealsPerWeek = context.Plan.MealsPerWeek;

        if (menu.Status == MenuStatus.Confirmed)
        {
            // Menú confirmado: no se redistribuye nada, se reporta lo que quedó en los snapshots.
            var snapshots = menu.Dishes.Where(d => d.Snapshot is not null).Select(d => d.Snapshot!).ToList();
            var fixedDishes = snapshots
                .Select(s => new LegumeDish(
                    s.RecipeId,
                    s.Servings,
                    s.Batch.Any(l => l.Group == ComponentGroup.Leguminosa) ? LegumeMode.Included : LegumeMode.None))
                .ToList();
            var reference = LegumeBalancer.Balance(fixedDishes, frequency, mealsPerWeek);
            var legume = snapshots.Sum(s => s.Batch.Where(l => l.Group == ComponentGroup.Leguminosa).Select(l => l.Servings).DefaultIfEmpty(0).Max());
            var perWeek = reference.TotalServings == 0 ? 0 : Math.Round(legume / (double)reference.TotalServings * mealsPerWeek, 2);
            return reference with
            {
                SideServings = menu.Dishes.ToDictionary(d => d.RecipeId, d => d.LegumeSideServings),
                LegumeServings = legume,
                MealsPerWeekEquivalent = perWeek,
                Warnings = [],
            };
        }

        var dishes = menu.Dishes
            .Select(d => context.Recipes.FirstOrDefault(r => r.Id == d.RecipeId))
            .Where(r => r is not null)
            .Select(r => new LegumeDish(r!.Id, r.Servings, r.LegumeMode))
            .ToList();

        return LegumeBalancer.Balance(dishes, frequency, mealsPerWeek);
    }

    private GenerationOptions OptionsFor(NutritionPlan plan, int dishCount)
    {
        var frequency = plan.GetMeal(Meals.Comida)?.GetSlot(ComponentGroup.Leguminosa)?.WeeklyFrequency;
        if (frequency is null || plan.MealsPerWeek <= 0)
        {
            return Options;
        }

        // Cuántos guisados con leguminosa caben sin pasarse del máximo semanal (suponiendo lotes de 4),
        // y cuántas porciones deben poder llevarla para llegar al mínimo.
        var totalServings = dishCount * 4;
        var maxServings = Math.Floor(totalServings * (double)frequency.Max / plan.MealsPerWeek);
        var maxDishes = Math.Max(1, (int)Math.Floor(maxServings / 4));
        var minServings = (int)Math.Ceiling((totalServings * (double)frequency.Min / plan.MealsPerWeek) - 1e-9);

        return new GenerationOptions
        {
            MaxSameProtein = Options.MaxSameProtein,
            MaxLaborious = Options.MaxLaborious,
            RequireFishFrom = Options.RequireFishFrom,
            FishFamilies = Options.FishFamilies,
            RecentDays = Options.RecentDays,
            RecentPenalty = Options.RecentPenalty,
            SeasonBonus = Options.SeasonBonus,
            MaxLegumeIncludedDishes = Math.Min(Options.MaxLegumeIncludedDishes, maxDishes),
            MinLegumeServings = minServings,
        };
    }

    private static List<RecipeCandidate> BuildCandidates(MenuContext context) =>
        context.Recipes
            .Where(r => string.Equals(r.Meal, Meals.Comida, StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                var proteinId = r.GetComponent(ComponentGroup.Proteina)?.Ingredients.FirstOrDefault()?.IngredientId;
                var family = proteinId is not null && context.Ingredients.TryGetValue(proteinId, out var protein)
                    ? protein.ProteinFamily
                    : null;
                var forbidden = context.Plan.Rules.ForbiddenMethods.Contains(r.CookingMethod, StringComparer.OrdinalIgnoreCase);
                return new RecipeCandidate(r, family, Compatible: !forbidden && proteinId is not null);
            })
            .ToList();

    private async Task<MenuContext> LoadContextAsync(CancellationToken ct)
    {
        var plan = await plans.GetActiveAsync(ct)
            ?? throw new NotFoundException("No hay un plan de alimentación activo. Carga el seed o crea uno en /api/plans.");
        var ingredientList = await ingredients.ListAsync(ct);
        var recipeList = await recipes.ListAsync(ct: ct);
        var pantry = await settings.GetPantryAsync(ct);
        var basics = await settings.GetBasicsAsync(ct);
        return new MenuContext(plan, ingredientList.ToDictionary(i => i.Id, StringComparer.Ordinal), recipeList, pantry, basics);
    }

    private async Task<WeeklyMenu> RequireMenuAsync(string id, CancellationToken ct) =>
        await menus.GetAsync(id, ct) ?? throw new NotFoundException($"No existe el menú '{id}'.");

    private async Task<WeeklyMenu> RequireDraftAsync(string id, CancellationToken ct)
    {
        var menu = await RequireMenuAsync(id, ct);
        if (menu.Status != MenuStatus.Draft)
        {
            throw new ConflictException("El menú ya está confirmado; genera uno nuevo para hacer cambios.");
        }

        return menu;
    }

    public static DateOnly NextMonday(DateOnly today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    public static string FormatWeek(DateOnly weekOf) =>
        weekOf.ToString("d 'de' MMMM", CultureInfo.GetCultureInfo("es-MX"));

    private sealed record MenuContext(
        NutritionPlan Plan,
        IReadOnlyDictionary<string, Ingredient> Ingredients,
        List<Recipe> Recipes,
        Pantry Pantry,
        BasicsConfig Basics);
}
