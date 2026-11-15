using MenuPlanner.Application;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MenuPlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMenuPlannerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));
        services.AddSingleton<MongoContext>();
        services.AddSingleton<IPlanRepository, PlanRepository>();
        services.AddSingleton<IIngredientRepository, IngredientRepository>();
        services.AddSingleton<IRecipeRepository, RecipeRepository>();
        services.AddSingleton<IMenuRepository, MenuRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IDishRepository, DishRepository>();
        services.AddSingleton<IImportRepository, ImportRepository>();
        return services;
    }

    /// <summary>
    /// Crea índices y carga el seed en las colecciones vacías (idempotente: nunca sobrescribe tus datos).
    /// </summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider services, string? seedDirectory, ILogger logger, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<MongoContext>();
        await db.EnsureIndexesAsync(ct);

        if (string.IsNullOrWhiteSpace(seedDirectory) || !Directory.Exists(seedDirectory))
        {
            logger.LogInformation("Seed deshabilitado o no encontrado ({Directory}).", seedDirectory);
            return;
        }

        var seed = SeedData.Load(seedDirectory);

        if (await db.Ingredients.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Ingredients.Count > 0)
        {
            await db.Ingredients.InsertManyAsync(seed.Ingredients, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} ingredientes cargados.", seed.Ingredients.Count);
        }

        if (await db.Plans.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Plans.Count > 0)
        {
            await db.Plans.InsertManyAsync(seed.Plans, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} planes cargados.", seed.Plans.Count);
        }

        if (await db.Recipes.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Recipes.Count > 0)
        {
            await db.Recipes.InsertManyAsync(seed.Recipes, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} recetas cargadas.", seed.Recipes.Count);
        }

        if (await db.Dishes.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Dishes.Count > 0)
        {
            await db.Dishes.InsertManyAsync(seed.Dishes, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} platillos del diccionario de Keep cargados.", seed.Dishes.Count);
        }

        if (await db.ImportRules.EstimatedDocumentCountAsync(cancellationToken: ct) == 0)
        {
            seed.ImportRules.Id = "default";
            await db.ImportRules.InsertOneAsync(seed.ImportRules, cancellationToken: ct);
        }
    }
}
