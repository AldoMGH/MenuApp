using MenuPlanner.Domain;

namespace MenuPlanner.Contracts;

public sealed record ReviewMapping(string Name, string? DishId = null, string? NewDishName = null, bool Ignore = false);

public sealed record ReviewRequest(IReadOnlyList<ReviewMapping> Mappings);

public sealed record PantrySuggestion(
    string? IngredientId,
    string Name,
    NoteSection Section,
    int WeeksBought,
    int WeeksInWindow,
    double Share,
    DateTime LastBought,
    double? TypicalQty,
    Unit? TypicalUnit,
    bool InPantry,
    bool InBasics);

public sealed record DishView(Dish Dish, string? RecipeName);

public sealed record SeedSyncResult(
    int IngredientsAdded,
    int RecipesUpserted,
    int RecipesRemoved,
    int DishesAdded,
    int DishesUpdated,
    int RulesAdded,
    int PlansAdded);

/// <summary>Receta que puedes cocinar con lo que sueles tener: cobertura (0–1) y lo que falta comprar.</summary>
public sealed record CookableRecipe(
    string RecipeId,
    string Name,
    double Coverage,
    IReadOnlyList<string> Have,
    IReadOnlyList<string> Missing,
    double? KcalPerServing);
