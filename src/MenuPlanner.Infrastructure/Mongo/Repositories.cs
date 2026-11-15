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

    public async Task SetHistoryAsync(IReadOnlyDictionary<string, RecipeHistory> history, CancellationToken ct = default)
    {
        var models = new List<WriteModel<Recipe>>
        {
            new UpdateManyModel<Recipe>(
                Builders<Recipe>.Filter.Nin(r => r.Id, history.Keys),
                Builders<Recipe>.Update
                    .Set(r => r.Stats.HistoryCount, 0)
                    .Set(r => r.Stats.HistoryLastCooked, null)
                    .Set(r => r.Stats.HistoryMonths, new List<int>())),
        };
        models.AddRange(history.Select(h => new UpdateOneModel<Recipe>(
            Builders<Recipe>.Filter.Eq(r => r.Id, h.Key),
            Builders<Recipe>.Update
                .Set(r => r.Stats.HistoryCount, h.Value.Count)
                .Set(r => r.Stats.HistoryLastCooked, h.Value.LastCooked)
                .Set(r => r.Stats.HistoryMonths, h.Value.Months.ToList()))));
        await db.Recipes.BulkWriteAsync(models, cancellationToken: ct);
    }
}

public sealed class DishRepository(MongoContext db) : IDishRepository
{
    public async Task<List<Dish>> ListAsync(CancellationToken ct = default) =>
        await db.Dishes.Find(FilterDefinition<Dish>.Empty).SortBy(d => d.Name).ToListAsync(ct);

    public async Task<Dish?> GetAsync(string id, CancellationToken ct = default) =>
        await db.Dishes.Find(d => d.Id == id).FirstOrDefaultAsync(ct);

    public Task UpsertAsync(Dish dish, CancellationToken ct = default) =>
        db.Dishes.ReplaceOneAsync(d => d.Id == dish.Id, dish, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task UpsertManyAsync(IEnumerable<Dish> dishes, CancellationToken ct = default)
    {
        var models = dishes
            .Select(d => new ReplaceOneModel<Dish>(Builders<Dish>.Filter.Eq(x => x.Id, d.Id), d) { IsUpsert = true })
            .ToList();
        if (models.Count > 0)
        {
            await db.Dishes.BulkWriteAsync(models, cancellationToken: ct);
        }
    }
}

public sealed class ImportRepository(MongoContext db) : IImportRepository
{
    public async Task<HashSet<string>> GetExistingNoteIdsAsync(IEnumerable<string> noteIds, CancellationToken ct = default)
    {
        var ids = noteIds.Distinct().ToList();
        var found = await db.ImportedNotes.Find(Builders<ImportedNote>.Filter.In(n => n.Id, ids))
            .Project(n => n.Id)
            .ToListAsync(ct);
        return found.ToHashSet(StringComparer.Ordinal);
    }

    public async Task SaveImportAsync(
        ImportJob job,
        IReadOnlyList<ImportedNote> notes,
        IReadOnlyList<DishOccurrence> occurrences,
        IReadOnlyList<PurchaseRecord> purchases,
        CancellationToken ct = default)
    {
        // Mongo local sin replica set: sin transacción. El orden (historial → notas → resumen) hace que un
        // fallo a la mitad solo deje renglones que se vuelven a escribir al reintentar (ids deterministas).
        var unordered = new InsertManyOptions { IsOrdered = false };
        if (occurrences.Count > 0)
        {
            await Upsert(db.DishHistory, occurrences, o => o.Id, ct);
        }

        if (purchases.Count > 0)
        {
            await Upsert(db.PurchaseHistory, purchases, p => p.Id, ct);
        }

        if (notes.Count > 0)
        {
            try
            {
                await db.ImportedNotes.InsertManyAsync(notes, unordered, ct);
            }
            catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
            {
                // Otra importación ya registró esas notas: es idempotente.
            }
        }

        await SaveJobAsync(job, ct);
    }

    public async Task<List<DishOccurrence>> ListOccurrencesAsync(string? importId = null, CancellationToken ct = default)
    {
        var filter = importId is null
            ? FilterDefinition<DishOccurrence>.Empty
            : Builders<DishOccurrence>.Filter.Eq(o => o.ImportId, importId);
        return await db.DishHistory.Find(filter).SortByDescending(o => o.Date).ToListAsync(ct);
    }

    public Task UpdateOccurrencesAsync(IEnumerable<DishOccurrence> occurrences, CancellationToken ct = default)
    {
        var list = occurrences.ToList();
        return list.Count == 0 ? Task.CompletedTask : Upsert(db.DishHistory, list, o => o.Id, ct);
    }

    public async Task<List<PurchaseRecord>> ListPurchasesAsync(CancellationToken ct = default) =>
        await db.PurchaseHistory.Find(FilterDefinition<PurchaseRecord>.Empty).ToListAsync(ct);

    public async Task<ImportJob?> GetJobAsync(string id, CancellationToken ct = default) =>
        await db.ImportJobs.Find(j => j.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<ImportJob>> ListJobsAsync(int limit = 20, CancellationToken ct = default) =>
        await db.ImportJobs.Find(FilterDefinition<ImportJob>.Empty)
            .SortByDescending(j => j.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

    public Task SaveJobAsync(ImportJob job, CancellationToken ct = default) =>
        db.ImportJobs.ReplaceOneAsync(j => j.Id == job.Id, job, new ReplaceOptions { IsUpsert = true }, ct);

    private static Task Upsert<T>(IMongoCollection<T> collection, IEnumerable<T> items, Func<T, string> id, CancellationToken ct) =>
        collection.BulkWriteAsync(
            items.Select(i => new ReplaceOneModel<T>(Builders<T>.Filter.Eq("_id", id(i)), i) { IsUpsert = true }),
            new BulkWriteOptions { IsOrdered = false },
            ct);
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

    public async Task<ImportRules> GetImportRulesAsync(CancellationToken ct = default) =>
        await db.ImportRules.Find(r => r.Id == DefaultId).FirstOrDefaultAsync(ct) ?? new ImportRules();

    public Task SaveImportRulesAsync(ImportRules rules, CancellationToken ct = default)
    {
        rules.Id = DefaultId;
        return db.ImportRules.ReplaceOneAsync(r => r.Id == DefaultId, rules, new ReplaceOptions { IsUpsert = true }, ct);
    }
}
