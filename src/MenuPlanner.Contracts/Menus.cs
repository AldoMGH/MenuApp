using MenuPlanner.Domain;

namespace MenuPlanner.Contracts;

public sealed record GenerateMenuRequest(
    int DishCount,
    DateOnly? WeekOf = null,
    int? Seed = null,
    IReadOnlyList<string>? MustInclude = null,
    IReadOnlyList<string>? Exclude = null);

public sealed record SwapCriteria(string? ProteinFamily = null, string? Tag = null);

public sealed record MenuSummary(
    int Dishes,
    int TotalServings,
    int MealsPerWeek,
    int SurplusServings,
    int LegumeServings,
    int LegumeMinServings,
    int LegumeMaxServings,
    double LegumeMealsPerWeek,
    IReadOnlyList<string> Warnings);

public sealed record MenuDishView(string RecipeId, string Name, int LegumeSideServings, PortionedRecipe Portioned);

public sealed record MenuView(
    string Id,
    DateOnly WeekOf,
    MenuStatus Status,
    int Seed,
    string PlanId,
    int PlanVersion,
    IReadOnlyList<MenuDishView> Dishes,
    MenuSummary Summary);

public sealed record MenuListItem(string Id, DateOnly WeekOf, MenuStatus Status, IReadOnlyList<string> Dishes);

public sealed record AddDishRequest(string RecipeId);
