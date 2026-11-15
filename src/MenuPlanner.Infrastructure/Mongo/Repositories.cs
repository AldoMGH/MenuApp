using MenuPlanner.Application;
using MenuPlanner.Domain;
using MongoDB.Driver;

namespace MenuPlanner.Infrastructure.Mongo;

public sealed class PlanRepository(MongoContext db) : IPlanRepository
{
    public async Task<NutritionPlan?> GetActiveAsync(CancellationToken ct = default) =>
        await db.Plans.Find(p => p.Active).SortByDescending(p => p.Version).FirstOrDefaultAsync(ct);

    public async Task<NutritionPlan?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Plans.Find(p => p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<NutritionPlan>> ListAsync(CancellationToken ct = default) =>
        await db.Plans.Find(FilterDefinition<NutritionPlan>.Empty).SortByDescending(p => p.Version).ToListAsync(ct);

    public Task UpsertAsync(NutritionPlan plan, CancellationToken ct = default) =>
        db.Plans.ReplaceOneAsync(p => p.Id == plan.Id, plan, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task ActivateAsync(string id, CancellationToken ct = default)
    {
        // Mongo local sin replica set: no hay transacción; primero se activa el nuevo y luego se apagan los demás.
        var result = await db.Plans.UpdateOneAsync(p => p.Id == id, Builders<NutritionPlan>.Update.Set(p => p.Active, true), cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            throw new NotFoundException($"No existe el plan '{id}'.");
        }

        await db.Plans.UpdateManyAsync(p => p.Id != id, Builders<NutritionPlan>.Update.Set(p => p.Active, false), cancellationToken: ct);
    }
}

public sealed class IngredientRepository(MongoContext db) : IIngredientRepository
{
    public async Task<List<Ingredient>> ListAsync(CancellationToken ct = default) =>
        await db.Ingredients.Find(FilterDefinition<Ingredient>.Empty).SortBy(i => i.Name).ToListAsync(ct);

    public async Task<Ingredient?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Ingredients.Find(i => i.Id == id).FirstOrDefaultAsync(ct);

    public Task UpsertAsync(Ingredient ingredient, CancellationToken ct = default) =>
        db.Ingredients.ReplaceOneAsync(i => i.Id == ingredient.Id, ingredient, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task UpsertManyAsync(IEnumerable<Ingredient> ingredients, CancellationToken ct = default)
    {
        var models = ingredients
            .Select(i => new ReplaceOneModel<Ingredient>(Builders<Ingredient>.Filter.Eq(x => x.Id, i.Id), i) { IsUpsert = true })
            .ToList();
        if (models.Count > 0)
        {
            await db.Ingredients.BulkWriteAsync(models, cancellationToken: ct);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) =>
        (await db.Ingredients.DeleteOneAsync(i => i.Id == id, ct)).DeletedCount > 0;
}

public sealed class RecipeRepository(MongoContext db) : IRecipeRepository
{
    public async Task<List<Recipe>> ListAsync(string? meal = null, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(meal)
            ? FilterDefinition<Recipe>.Empty
            : Builders<Recipe>.Filter.Eq(r => r.Meal, meal);
        return await db.Recipes.Find(filter).SortBy(r => r.Name).ToListAsync(ct);
    }

    public async Task<Recipe?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Recipes.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public Task UpsertAsync(Recipe recipe, CancellationToken ct = default) =>
        db.Recipes.ReplaceOneAsync(r => r.Id == recipe.Id, recipe, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task UpsertManyAsync(IEnumerable<Recipe> recipes, CancellationToken ct = default)
    {
        var models = recipes
            .Select(r => new ReplaceOneModel<Recipe>(Builders<Recipe>.Filter.Eq(x => x.Id, r.Id), r) { IsUpsert = true })
            .ToList();
        if (models.Count > 0)
        {
            await db.Recipes.BulkWriteAsync(models, cancellationToken: ct);
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) =>
        (await db.Recipes.DeleteOneAsync(r => r.Id == id, ct)).DeletedCount > 0;

    public Task RegisterCookedAsync(IEnumerable<string> recipeIds, DateTime date, CancellationToken ct = default)
    {
        var ids = recipeIds.Distinct().ToList();
        var update = Builders<Recipe>.Update
            .Inc(r => r.Stats.TimesCooked, 1)
            .Set(r => r.Stats.LastCooked, date);
        return db.Recipes.UpdateManyAsync(Builders<Recipe>.Filter.In(r => r.Id, ids), update, cancellationToken: ct);
    }
}

public sealed class MenuRepository(MongoContext db) : IMenuRepository
{
    public async Task<WeeklyMenu?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Menus.Find(m => m.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<WeeklyMenu>> ListAsync(int limit = 20, CancellationToken ct = default) =>
        await db.Menus.Find(FilterDefinition<WeeklyMenu>.Empty)
            .SortByDescending(m => m.WeekOf)
            .ThenByDescending(m => m.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

    public Task UpsertAsync(WeeklyMenu menu, CancellationToken ct = default) =>
        db.Menus.ReplaceOneAsync(m => m.Id == menu.Id, menu, new ReplaceOptions { IsUpsert = true }, ct);
}

public sealed class SettingsRepository(MongoContext db) : ISettingsRepository
{
    private const string DefaultId = "default";

    public async Task<Pantry> GetPantryAsync(CancellationToken ct = default) =>
        await db.Pantry.Find(p => p.Id == DefaultId).FirstOrDefaultAsync(ct) ?? new Pantry();

    public Task SavePantryAsync(Pantry pantry, CancellationToken ct = default)
    {
        pantry.Id = DefaultId;
        return db.Pantry.ReplaceOneAsync(p => p.Id == DefaultId, pantry, new ReplaceOptions { IsUpsert = true }, ct);
    }

    public async Task<BasicsConfig> GetBasicsAsync(CancellationToken ct = default) =>
        await db.Basics.Find(b => b.Id == DefaultId).FirstOrDefaultAsync(ct) ?? new BasicsConfig();

    public Task SaveBasicsAsync(BasicsConfig basics, CancellationToken ct = default)
    {
        basics.Id = DefaultId;
        return db.Basics.ReplaceOneAsync(b => b.Id == DefaultId, basics, new ReplaceOptions { IsUpsert = true }, ct);
    }
}
